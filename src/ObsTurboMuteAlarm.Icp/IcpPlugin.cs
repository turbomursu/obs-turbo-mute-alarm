namespace ObsTurboMuteAlarm.Icp;

/// <summary>Entry point the app looks for when it loads this plugin.</summary>
internal sealed class IcpPlugin : IIcpPlugin
{
    public bool IsDevicePresent()
    {
        try { return IcpHidDevice.IsPresent(); }
        catch { return false; }
    }

    public IIcpDisplay OpenDisplay() => new IcpDisplay();

    public string PreviewAscii() => DedRenderer.ToAscii(DedRenderer.BigMute(inverted: false));
}
