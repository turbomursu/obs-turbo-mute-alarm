using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ObsTurboMuteAlarm.Obs;

internal sealed record ObsAudioInput(string Name, string Uuid, string Kind, bool Muted);

internal sealed class ObsRequestException(string requestType, int code, string? comment)
    : Exception($"OBS request {requestType} failed: {code} {comment}")
{
    public int Code { get; } = code;
}

/// <summary>
/// Minimal obs-websocket v5 client that tracks the mute state of the watched
/// sources and reconnects automatically.
/// </summary>
internal sealed class ObsMuteMonitor : IDisposable
{
    private const int EventSubscriptionInputs = 1 << 3;
    private const int CloseAuthenticationFailed = 4009;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MinBackoff = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(15);

    private readonly ObsConfig _config;
    private readonly MuteStateTracker _tracker;
    private readonly object _trackerLock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> _pending = new();
    private ClientWebSocket? _ws;
    private Task? _loop;
    private bool _connected;
    private string? _error;

    public event Action<ObsStatus>? StatusChanged;

    public ObsMuteMonitor(ObsConfig config, IEnumerable<WatchedSource> sources)
    {
        _config = config;
        _tracker = new MuteStateTracker(sources);
    }

    public ObsStatus CurrentStatus
    {
        get { lock (_trackerLock) return new ObsStatus(_connected, _error, _tracker.Snapshot()); }
    }

    public void Start() => _loop ??= Task.Run(() => RunAsync(_cts.Token));

    private async Task RunAsync(CancellationToken ct)
    {
        var backoff = MinBackoff;
        string? lastLogged = null;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await ConnectAndRunAsync(ct, () => { backoff = MinBackoff; lastLogged = null; });
                SetDisconnected("Connection closed by OBS.");
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                string msg = ex is WebSocketException or HttpRequestException
                    ? $"Cannot connect to OBS at {_config.Host}:{_config.Port} (is OBS running with WebSocket server enabled?)"
                    : ex.Message;
                if (msg != lastLogged) Log.Info($"OBS: {msg}");
                lastLogged = msg;
                SetDisconnected(msg);
            }

            try { await Task.Delay(backoff, ct); }
            catch (OperationCanceledException) { break; }
            backoff = TimeSpan.FromTicks(Math.Min(backoff.Ticks * 2, MaxBackoff.Ticks));
        }
    }

    private async Task ConnectAndRunAsync(CancellationToken ct, Action onIdentified)
    {
        using var ws = new ClientWebSocket();
        using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectCts.CancelAfter(TimeSpan.FromSeconds(5));
            await ws.ConnectAsync(new Uri($"ws://{_config.Host}:{_config.Port}"), connectCts.Token);
        }

        var hello = await ReceiveAsync(ws, ct) ?? throw new InvalidOperationException("OBS closed the connection during handshake.");
        if (hello.GetProperty("op").GetInt32() != 0) throw new InvalidOperationException("Unexpected handshake message from OBS.");
        var helloData = hello.GetProperty("d");

        var identify = new JsonObject
        {
            ["rpcVersion"] = 1,
            ["eventSubscriptions"] = EventSubscriptionInputs,
        };
        if (helloData.TryGetProperty("authentication", out var auth))
        {
            string password = _config.ResolvePassword();
            if (password.Length == 0)
                throw new InvalidOperationException("OBS WebSocket requires a password: enter it in Settings.");
            identify["authentication"] = ObsAuth.Compute(password,
                auth.GetProperty("salt").GetString()!, auth.GetProperty("challenge").GetString()!);
        }
        await SendAsync(ws, new JsonObject { ["op"] = 1, ["d"] = identify }, ct);

        var identified = await ReceiveAsync(ws, ct);
        if (identified == null)
        {
            if ((int?)ws.CloseStatus == CloseAuthenticationFailed)
                throw new InvalidOperationException("OBS rejected the WebSocket password (check the password in Settings).");
            throw new InvalidOperationException($"OBS closed the connection: {ws.CloseStatusDescription}");
        }
        if (identified.Value.GetProperty("op").GetInt32() != 2) throw new InvalidOperationException("Unexpected identify response from OBS.");

        _ws = ws;
        onIdentified();
        lock (_trackerLock) { _connected = true; _error = null; }
        Log.Info($"OBS: connected to {_config.Host}:{_config.Port}.");

        var receiveLoop = ReceiveLoopAsync(ws, ct);
        try
        {
            await RefreshAsync(ct);
        }
        catch
        {
            ws.Abort();
            try { await receiveLoop; } catch { }
            throw;
        }
        await receiveLoop;
    }

    private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken ct)
    {
        try
        {
            while (true)
            {
                var msg = await ReceiveAsync(ws, ct);
                if (msg == null) return;
                var root = msg.Value;
                var d = root.GetProperty("d");
                switch (root.GetProperty("op").GetInt32())
                {
                    case 5:
                        HandleEvent(d);
                        break;
                    case 7:
                        if (_pending.TryRemove(d.GetProperty("requestId").GetString()!, out var tcs))
                            tcs.TrySetResult(d.Clone());
                        break;
                }
            }
        }
        finally
        {
            _ws = null;
            foreach (var key in _pending.Keys)
                if (_pending.TryRemove(key, out var tcs)) tcs.TrySetCanceled();
        }
    }

    private void HandleEvent(JsonElement d)
    {
        string type = d.GetProperty("eventType").GetString() ?? "";
        var data = d.TryGetProperty("eventData", out var ed) ? ed : default;
        switch (type)
        {
            case "InputMuteStateChanged":
                bool changed;
                lock (_trackerLock)
                {
                    changed = _tracker.ApplyMuteEvent(
                        GetString(data, "inputUuid"), GetString(data, "inputName"), data.GetProperty("inputMuted").GetBoolean());
                }
                if (changed) Publish();
                break;
            case "InputCreated":
            case "InputRemoved":
            case "InputNameChanged":
                _ = RefreshSafeAsync();
                break;
        }
    }

    private async Task RefreshSafeAsync()
    {
        try { await RefreshAsync(_cts.Token); }
        catch (Exception ex) when (ex is not OperationCanceledException) { Log.Info($"OBS: refresh failed: {ex.Message}"); }
        catch (OperationCanceledException) { }
    }

    /// <summary>Re-resolves the watched sources against OBS's input list and queries their mute state.</summary>
    private async Task RefreshAsync(CancellationToken ct)
    {
        await _refreshLock.WaitAsync(ct);
        try
        {
            var list = await RequestAsync("GetInputList", null, ct);
            var inputs = list.GetProperty("inputs").EnumerateArray()
                .Select(i => new ObsInput(GetString(i, "inputName") ?? "", GetString(i, "inputUuid") ?? ""))
                .ToList();

            int count;
            lock (_trackerLock) { _tracker.Resolve(inputs); count = _tracker.Count; }

            for (int i = 0; i < count; i++)
            {
                ObsInput? input;
                int seq;
                lock (_trackerLock) { input = _tracker.ResolvedInput(i); seq = _tracker.EventSeq(i); }
                if (input is not { } inp) continue;

                var req = !string.IsNullOrEmpty(inp.Uuid)
                    ? new JsonObject { ["inputUuid"] = inp.Uuid }
                    : new JsonObject { ["inputName"] = inp.Name };
                JsonElement resp;
                try { resp = await RequestAsync("GetInputMute", req, ct); }
                catch (ObsRequestException ex)
                {
                    Log.Info($"OBS: cannot read mute state of '{inp.Name}': {ex.Message}");
                    continue;
                }
                lock (_trackerLock) _tracker.SetQueriedMuted(i, resp.GetProperty("inputMuted").GetBoolean(), seq);
            }

            var snapshot = CurrentStatus;
            foreach (var s in snapshot.Sources)
                Log.Info($"OBS: source '{s.DisplayName}': {(s.Found ? s.Muted == true ? "muted" : "live" : "NOT FOUND")}");
            Publish();
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<JsonElement> RequestAsync(string requestType, JsonObject? requestData, CancellationToken ct)
    {
        var ws = _ws ?? throw new InvalidOperationException("Not connected to OBS.");
        string id = Guid.NewGuid().ToString("N");
        var tcs = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;

        var d = new JsonObject { ["requestType"] = requestType, ["requestId"] = id };
        if (requestData != null) d["requestData"] = requestData;
        await SendAsync(ws, new JsonObject { ["op"] = 6, ["d"] = d }, ct);

        JsonElement resp;
        try { resp = await tcs.Task.WaitAsync(RequestTimeout, ct); }
        finally { _pending.TryRemove(id, out _); }

        var status = resp.GetProperty("requestStatus");
        if (!status.GetProperty("result").GetBoolean())
            throw new ObsRequestException(requestType, status.GetProperty("code").GetInt32(), GetString(status, "comment"));
        return resp.TryGetProperty("responseData", out var rd) ? rd : default;
    }

    /// <summary>
    /// Lists OBS inputs that have audio (those GetInputMute accepts) with their current mute state.
    /// </summary>
    public async Task<IReadOnlyList<ObsAudioInput>> ListAudioInputsAsync(CancellationToken ct)
    {
        var list = await RequestAsync("GetInputList", null, ct).ConfigureAwait(false);
        var result = new List<ObsAudioInput>();
        foreach (var i in list.GetProperty("inputs").EnumerateArray())
        {
            string name = GetString(i, "inputName") ?? "";
            string uuid = GetString(i, "inputUuid") ?? "";
            var req = uuid.Length > 0 ? new JsonObject { ["inputUuid"] = uuid } : new JsonObject { ["inputName"] = name };
            try
            {
                var resp = await RequestAsync("GetInputMute", req, ct).ConfigureAwait(false);
                result.Add(new ObsAudioInput(name, uuid, GetString(i, "inputKind") ?? "", resp.GetProperty("inputMuted").GetBoolean()));
            }
            catch (ObsRequestException)
            {
                // Video-only input: it has no mute state.
            }
        }
        return result;
    }

    /// <summary>Connects once with the given settings and lists OBS's audio inputs.</summary>
    public static async Task<IReadOnlyList<ObsAudioInput>> FetchAudioInputsAsync(ObsConfig config, TimeSpan timeout, CancellationToken ct = default)
    {
        using var monitor = new ObsMuteMonitor(config, Array.Empty<WatchedSource>());
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        monitor.StatusChanged += s =>
        {
            if (s.Connected) ready.TrySetResult();
            else if (s.Error != null) ready.TrySetException(new InvalidOperationException(s.Error));
        };
        monitor.Start();
        await ready.Task.WaitAsync(timeout, ct).ConfigureAwait(false);
        return await monitor.ListAudioInputsAsync(ct).ConfigureAwait(false);
    }

    private async Task SendAsync(ClientWebSocket ws, JsonObject message, CancellationToken ct)
    {
        var bytes = Encoding.UTF8.GetBytes(message.ToJsonString());
        await _sendLock.WaitAsync(ct);
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, ct); }
        finally { _sendLock.Release(); }
    }

    /// <summary>Receives one JSON message, or null if the socket was closed.</summary>
    private static async Task<JsonElement?> ReceiveAsync(ClientWebSocket ws, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        using var ms = new MemoryStream();
        while (true)
        {
            var result = await ws.ReceiveAsync(buffer, ct);
            if (result.MessageType == WebSocketMessageType.Close) return null;
            ms.Write(buffer, 0, result.Count);
            if (result.EndOfMessage) break;
        }
        using var doc = JsonDocument.Parse(ms.ToArray());
        return doc.RootElement.Clone();
    }

    private static string? GetString(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString()
            : null;

    private void SetDisconnected(string error)
    {
        bool wasConnected;
        lock (_trackerLock)
        {
            wasConnected = _connected;
            _connected = false;
            if (wasConnected) Log.Info("OBS: disconnected.");
            bool changed = wasConnected || _error != error;
            _error = error;
            _tracker.Reset();
            if (!changed) return;
        }
        Publish();
    }

    private readonly object _publishLock = new();

    private void Publish()
    {
        // Snapshot and notify under one lock so subscribers never see an older state last.
        lock (_publishLock) StatusChanged?.Invoke(CurrentStatus);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(TimeSpan.FromSeconds(2)); } catch { }
        _cts.Dispose();
    }
}
