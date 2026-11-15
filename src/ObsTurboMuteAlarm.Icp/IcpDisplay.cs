// Connection handling pattern adapted from BmsLightBridge
// (https://github.com/RedMeKool/BmsLightBridge), Services/Icp/IcpService.cs.
// MIT License, Copyright (c) 2026 RedMeKool — see THIRD-PARTY-NOTICES.md for the full notice.

using ObsTurboMuteAlarm.Warning;

namespace ObsTurboMuteAlarm.Icp;

/// <summary>
/// Owns the ICP HID connection, reconnects automatically and re-sends the
/// current frame after a reconnect. Thread-safe.
/// </summary>
internal sealed class IcpDisplay : IIcpDisplay
{
    private const int DevicePollMs = 2000;

    private readonly object _lock = new();
    private readonly System.Threading.Timer _pollTimer;
    private IcpHidDevice? _device;
    private byte[] _currentFrame = DedRenderer.Blank();
    private byte? _currentBrightness;
    private bool _disposed;

    public bool IsConnected { get; private set; }

    /// <summary>Raised (on a thread-pool thread) when the ICP connects or disconnects.</summary>
    public event Action<bool>? ConnectionChanged;

    public IcpDisplay()
    {
        _pollTimer = new System.Threading.Timer(_ => Poll(), null, 0, DevicePollMs);
    }

    /// <summary>
    /// Shows a 200x65 frame produced by <see cref="DedRenderer"/>. If <paramref name="screenBrightness"/>
    /// is given, the DED backlight is set to it as well (and re-applied after a reconnect).
    /// </summary>
    public void Show(byte[] frame, byte? screenBrightness = null)
    {
        lock (_lock)
        {
            if (_disposed) return;
            _currentFrame = frame;
            _currentBrightness = screenBrightness;
            if (_device != null) Send(frame, screenBrightness);
        }
    }

    private void Poll()
    {
        lock (_lock)
        {
            if (_disposed || _device != null) return;
            try
            {
                _device = new IcpHidDevice();
            }
            catch
            {
                _device = null;
                return;
            }
            SetConnected(true);
            Log.Info("ICP connected.");
            Send(_currentFrame, _currentBrightness);
        }
    }

    private void Send(byte[] frame, byte? screenBrightness = null)
    {
        if (_device == null) return;
        try
        {
            if (screenBrightness is { } b)
                _device.SetBrightness(IcpHidDevice.ScreenBacklightChannel, b);
            _device.WriteDedCommands(new[]
            {
                new DedCommand { CommandType = DedCommand.CmdWriteDisplayMem, DataBuffer = frame },
                new DedCommand { CommandType = DedCommand.CmdRefreshDisplay, DataBuffer = DedCommand.RefreshPayload },
            });
        }
        catch (Exception ex)
        {
            Log.Info($"ICP write failed ({ex.GetType().Name}); will reconnect.");
            CloseDevice();
        }
    }

    private void CloseDevice()
    {
        try { _device?.Dispose(); } catch { }
        _device = null;
        SetConnected(false);
    }

    private void SetConnected(bool connected)
    {
        if (IsConnected == connected) return;
        IsConnected = connected;
        ThreadPool.QueueUserWorkItem(_ => ConnectionChanged?.Invoke(connected));
    }

    public IWarningOutput CreateOutput(byte warningBrightness, byte? brightnessAfter) =>
        new IcpOutput(this, warningBrightness, brightnessAfter);

    public void Dispose()
    {
        _pollTimer.Dispose();
        lock (_lock)
        {
            if (_disposed) return;
            Send(DedRenderer.Blank());
            _disposed = true;
            try { _device?.Dispose(); } catch { }
            _device = null;
        }
    }
}
