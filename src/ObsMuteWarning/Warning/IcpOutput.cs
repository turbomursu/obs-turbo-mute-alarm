using ObsMuteWarning.Icp;

namespace ObsMuteWarning.Warning;

/// <summary>
/// Shows the warning on the WinWing ViperAce ICP DED: a big "MUTE" alternating between
/// normal and inverse video, with the screen backlight raised while it flashes.
/// </summary>
internal sealed class IcpOutput : IWarningOutput
{
    private static readonly byte[] NormalFrame = DedRenderer.BigMute(inverted: false);
    private static readonly byte[] InverseFrame = DedRenderer.BigMute(inverted: true);

    private readonly IcpDisplay _display;
    private readonly byte _warningBrightness;
    private readonly byte? _brightnessAfter;
    private bool _shown;

    public IcpOutput(IcpDisplay display, byte warningBrightness, byte? brightnessAfter)
    {
        _display = display;
        _warningBrightness = warningBrightness;
        _brightnessAfter = brightnessAfter;
    }

    public void Show(bool alternatePhase, IReadOnlyList<string> mutedSources)
    {
        _shown = true;
        _display.Show(alternatePhase ? InverseFrame : NormalFrame, _warningBrightness);
    }

    public void Hide()
    {
        if (!_shown) return;
        _shown = false;
        _display.Show(DedRenderer.Blank(), _brightnessAfter);
    }

    // The IcpDisplay is owned by the tray app and outlives settings changes.
    public void Dispose() { }
}
