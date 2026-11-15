using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ObsMuteWarning.Obs;

namespace ObsMuteWarning.Tests;

/// <summary>Runs the monitor against a minimal fake obs-websocket v5 server.</summary>
public class ObsMuteMonitorTests
{
    private const string Password = "pw";
    private const string Salt = "salt";
    private const string Challenge = "challenge";

    [Fact]
    public async Task AuthenticatesQueriesInitialStateAndFollowsMuteEvents()
    {
        int port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var statuses = new List<ObsStatus>();
        var gotMuted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var gotLiveAgain = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var identifyAuth = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        using var monitor = new ObsMuteMonitor(
            new ObsConfig { Host = "localhost", Port = port, Password = Password },
            new[]
            {
                new WatchedSource { Name = "Desk mic", Uuid = "uuid-mic" },
                new WatchedSource { Name = "Game /Äänet", Uuid = "stale-uuid" },
            });
        monitor.StatusChanged += s =>
        {
            lock (statuses) statuses.Add(s);
            if (s.Connected && s.AnyMuted) gotMuted.TrySetResult();
            if (gotMuted.Task.IsCompleted && s.Connected && !s.AnyMuted) gotLiveAgain.TrySetResult();
        };

        var server = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
            await Send(ws, new JsonObject
            {
                ["op"] = 0,
                ["d"] = new JsonObject
                {
                    ["obsWebSocketVersion"] = "5.7.4",
                    ["rpcVersion"] = 1,
                    ["authentication"] = new JsonObject { ["salt"] = Salt, ["challenge"] = Challenge },
                },
            });
            var identify = await Receive(ws);
            identifyAuth.TrySetResult(identify["d"]!["authentication"]?.GetValue<string>());
            await Send(ws, new JsonObject { ["op"] = 2, ["d"] = new JsonObject { ["negotiatedRpcVersion"] = 1 } });

            // Answer GetInputList + 2x GetInputMute (game source resolved by name: stale UUID).
            for (int i = 0; i < 3; i++)
            {
                var req = (await Receive(ws))["d"]!;
                string type = req["requestType"]!.GetValue<string>();
                JsonObject data = type switch
                {
                    "GetInputList" => new JsonObject
                    {
                        ["inputs"] = new JsonArray(
                            new JsonObject { ["inputName"] = "Desk mic", ["inputUuid"] = "uuid-mic" },
                            new JsonObject { ["inputName"] = "Game /Äänet", ["inputUuid"] = "uuid-game" },
                            new JsonObject { ["inputName"] = "Desktop Audio", ["inputUuid"] = "uuid-desktop" }),
                    },
                    "GetInputMute" => new JsonObject { ["inputMuted"] = false },
                    _ => throw new InvalidOperationException(type),
                };
                if (type == "GetInputMute")
                    Assert.Contains(req["requestData"]!["inputUuid"]!.GetValue<string>(), new[] { "uuid-mic", "uuid-game" });
                await Send(ws, new JsonObject
                {
                    ["op"] = 7,
                    ["d"] = new JsonObject
                    {
                        ["requestType"] = type,
                        ["requestId"] = req["requestId"]!.GetValue<string>(),
                        ["requestStatus"] = new JsonObject { ["result"] = true, ["code"] = 100 },
                        ["responseData"] = data,
                    },
                });
            }

            await Send(ws, MuteEvent("Desktop Audio", "uuid-desktop", true));
            await Send(ws, MuteEvent("Game /Äänet", "uuid-game", true));
            await gotMuted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Send(ws, MuteEvent("Game /Äänet", "uuid-game", false));
            await gotLiveAgain.Task.WaitAsync(TimeSpan.FromSeconds(5));
        });

        monitor.Start();
        await server.WaitAsync(TimeSpan.FromSeconds(15));

        Assert.Equal(ObsAuth.Compute(Password, Salt, Challenge), await identifyAuth.Task);
        lock (statuses)
        {
            Assert.Contains(statuses, s => s.Connected && !s.AnyMuted && s.Sources.All(x => x.Found && x.Muted == false));
            var last = statuses[^1];
            Assert.True(last.Connected);
            Assert.False(last.AnyMuted);
        }
    }

    [Fact]
    public async Task FetchAudioInputs_SkipsInputsWithoutAudio()
    {
        int port = FreePort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://localhost:{port}/");
        listener.Start();

        var server = Task.Run(async () =>
        {
            var ctx = await listener.GetContextAsync();
            var ws = (await ctx.AcceptWebSocketAsync(null)).WebSocket;
            await Send(ws, new JsonObject { ["op"] = 0, ["d"] = new JsonObject { ["rpcVersion"] = 1 } });
            await Receive(ws);
            await Send(ws, new JsonObject { ["op"] = 2, ["d"] = new JsonObject { ["negotiatedRpcVersion"] = 1 } });
            while (ws.State == WebSocketState.Open)
            {
                JsonNode req;
                try { req = (await Receive(ws))["d"]!; }
                catch (Exception) { return; } // client closed the socket
                if (req["requestType"] == null) return; // close frame
                string type = req["requestType"]!.GetValue<string>();
                string? uuid = req["requestData"]?["inputUuid"]?.GetValue<string>();
                bool ok = !(type == "GetInputMute" && uuid == "uuid-cam");
                JsonObject data = type == "GetInputList"
                    ? new JsonObject
                    {
                        ["inputs"] = new JsonArray(
                            new JsonObject { ["inputName"] = "Mic", ["inputUuid"] = "uuid-mic", ["inputKind"] = "wasapi_input_capture" },
                            new JsonObject { ["inputName"] = "Webcam", ["inputUuid"] = "uuid-cam", ["inputKind"] = "dshow_input" }),
                    }
                    : new JsonObject { ["inputMuted"] = true };
                await Send(ws, new JsonObject
                {
                    ["op"] = 7,
                    ["d"] = new JsonObject
                    {
                        ["requestType"] = type,
                        ["requestId"] = req["requestId"]!.GetValue<string>(),
                        ["requestStatus"] = ok
                            ? new JsonObject { ["result"] = true, ["code"] = 100 }
                            : new JsonObject { ["result"] = false, ["code"] = 604, ["comment"] = "The specified input does not support audio." },
                        ["responseData"] = ok ? data : null,
                    },
                });
            }
        });

        var inputs = await ObsMuteMonitor.FetchAudioInputsAsync(
            new ObsConfig { Host = "localhost", Port = port }, TimeSpan.FromSeconds(10));

        var mic = Assert.Single(inputs);
        Assert.Equal(new ObsAudioInput("Mic", "uuid-mic", "wasapi_input_capture", true), mic);
        await server.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private static JsonObject MuteEvent(string name, string uuid, bool muted) => new()
    {
        ["op"] = 5,
        ["d"] = new JsonObject
        {
            ["eventType"] = "InputMuteStateChanged",
            ["eventIntent"] = 8,
            ["eventData"] = new JsonObject { ["inputName"] = name, ["inputUuid"] = uuid, ["inputMuted"] = muted },
        },
    };

    private static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static Task Send(WebSocket ws, JsonObject msg) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(msg.ToJsonString()), WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<JsonNode> Receive(WebSocket ws)
    {
        var buf = new byte[16384];
        using var ms = new MemoryStream();
        WebSocketReceiveResult r;
        do
        {
            r = await ws.ReceiveAsync(buf, CancellationToken.None);
            ms.Write(buf, 0, r.Count);
        } while (!r.EndOfMessage);
        return JsonNode.Parse(ms.ToArray())!;
    }
}
