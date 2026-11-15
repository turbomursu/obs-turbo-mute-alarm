using ObsTurboMuteAlarm.Icp;

namespace ObsTurboMuteAlarm.Tests;

public class IcpSupportTests
{
    [Fact]
    public void LoadsPluginFromAppFolder()
    {
        // The test output folder contains ObsTurboMuteAlarm.Icp.dll, like an install with ICP support.
        var plugin = IcpSupport.Plugin;

        Assert.IsType<IcpPlugin>(plugin);
        Assert.Contains("#", plugin!.PreviewAscii());
    }
}
