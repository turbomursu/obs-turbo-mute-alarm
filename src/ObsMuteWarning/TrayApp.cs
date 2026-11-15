using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using ObsMuteWarning.Icp;
using ObsMuteWarning.Obs;
using ObsMuteWarning.UI;
using ObsMuteWarning.Warning;

namespace ObsMuteWarning;

/// <summary>System-tray UI: status icon, menu, settings, and wiring of OBS state to the warning outputs.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private const string AppName = "OBS Mute Warning";

    private readonly NotifyIcon _tray;
    private readonly Control _invoker;
    private readonly WarningController _controller;
    private readonly ContextMenuStrip _menu = new();
    private readonly Icon _iconOffline = MakeIcon(Color.Gray);
    private readonly Icon _iconLive = MakeIcon(Color.LimeGreen);
    private readonly Icon _iconMuted = MakeIcon(Color.Red);
    private readonly string _settingsPath;
    private AppSettings _settings;
    private volatile ObsMuteMonitor? _monitor;
    private string? _monitorKey;
    private IcpDisplay? _icp;
    private SettingsForm? _settingsForm;

    public TrayApp(AppSettings settings, string settingsPath, bool firstRun, bool showTestWarning)
    {
        _settingsPath = settingsPath;
        _settings = settings;
        _invoker = new Control();
        _ = _invoker.Handle; // create the handle so BeginInvoke works

        _tray = new NotifyIcon { Icon = _iconOffline, Text = AppName, Visible = true, ContextMenuStrip = _menu };
        _tray.DoubleClick += (_, _) => OpenSettings();
        _controller = new WarningController(settings.FlashIntervalMs);

        Apply(settings, save: false);

        if (firstRun)
            _invoker.BeginInvoke(OpenSettings);
        if (showTestWarning)
            _controller.ShowTest(TimeSpan.FromSeconds(5));
    }

    /// <summary>Applies (and optionally saves) settings without restarting the app. UI thread.</summary>
    private void Apply(AppSettings s, bool save)
    {
        if (save) s.Save(_settingsPath);
        _settings = s;

        if (s.IcpEnabled && _icp == null)
        {
            _icp = new IcpDisplay();
            _icp.ConnectionChanged += _ => UpdateUi();
        }

        var style = new PopupStyle(s.PopupText, s.PopupPosition, s.PopupSize, s.PopupOpacityPercent, s.PopupClickThrough);
        var outputs = new List<IWarningOutput>();
        outputs.AddRange(s.Monitors.Select(m => new PopupOutput(_invoker, m, style)));
        if (s.IcpEnabled) outputs.Add(new IcpOutput(_icp!, s.IcpWarningBrightness, s.IcpBrightnessAfterWarning));
        _controller.Configure(outputs, s.FlashIntervalMs);

        if (!s.IcpEnabled && _icp != null)
        {
            _icp.Dispose(); // blanks the DED
            _icp = null;
        }

        // Reconnect to OBS only if the connection or the watched sources changed.
        string key = string.Join("|", s.Obs.Host, s.Obs.Port, s.Obs.Password,
            string.Join(";", s.Sources.Select(x => $"{x.Uuid}/{x.Name}")));
        if (key != _monitorKey)
        {
            _monitorKey = key;
            var old = _monitor;
            var monitor = new ObsMuteMonitor(s.Obs, s.Sources);
            monitor.StatusChanged += status => OnObsStatus(monitor, status);
            _monitor = monitor;
            old?.Dispose();
            _controller.SetMutedSources(Array.Empty<string>());
            Log.Info($"Watching: {(s.Sources.Count == 0 ? "(nothing)" : string.Join(", ", s.Sources.Select(x => $"'{x}'")))}");
            monitor.Start();
        }

        Log.Info($"Warning outputs: {DescribeOutputs(s)}");
        UpdateUi();
    }

    private void OnObsStatus(ObsMuteMonitor sender, ObsStatus status)
    {
        if (!ReferenceEquals(sender, _monitor)) return; // replaced by a newer monitor
        _controller.SetMutedSources(status.Sources.Where(x => x.Muted == true).Select(x => x.DisplayName).ToList());
        UpdateUi();
    }

    private void OpenSettings()
    {
        if (_settingsForm is { IsDisposed: false })
        {
            _settingsForm.Activate();
            return;
        }
        _settingsForm = new SettingsForm(_settings,
            s => Apply(s, save: true),
            () => _controller.ShowTest(TimeSpan.FromSeconds(5)));
        _settingsForm.FormClosed += (_, _) => _settingsForm = null;
        _settingsForm.Show();
        _settingsForm.Activate();
    }

    private static string DescribeOutputs(AppSettings s)
    {
        var parts = s.Monitors.Select(m => $"Monitor {SettingsForm.MonitorNumber(m)}").ToList();
        if (s.IcpEnabled) parts.Add("ICP");
        return parts.Count == 0 ? "none" : string.Join(", ", parts);
    }

    private void UpdateUi()
    {
        if (_invoker.IsDisposed) return;
        if (_invoker.InvokeRequired)
        {
            try { _invoker.BeginInvoke(UpdateUi); } catch (InvalidOperationException) { }
            return;
        }

        var status = _monitor?.CurrentStatus;
        bool connected = status?.Connected == true;
        bool muted = connected && status!.AnyMuted;
        _tray.Icon = !connected ? _iconOffline : muted ? _iconMuted : _iconLive;

        string obsLine = connected ? "OBS: connected" : "OBS: disconnected";
        string state = _settings.Sources.Count == 0 ? "No sources selected"
            : !connected ? "" : muted ? "MUTED" : "All live";
        string? icpLine = _icp == null ? null : _icp.IsConnected ? "ICP: connected" : "ICP: not found";
        var tip = string.Join("\n", new[] { AppName, obsLine, icpLine, state }.Where(x => !string.IsNullOrEmpty(x)));
        _tray.Text = tip.Length > 127 ? tip[..127] : tip;

        _menu.Items.Clear();
        AddInfo(obsLine);
        if (!connected && status?.Error != null) AddInfo("   " + status.Error);
        foreach (var s in status?.Sources ?? Array.Empty<SourceStatus>())
        {
            string st = !connected ? "?" : !s.Found ? "not found" : s.Muted == true ? "MUTED" : s.Muted == false ? "live" : "?";
            AddInfo($"   {s.DisplayName}: {st}");
        }
        if (_settings.Sources.Count == 0) AddInfo("   No sources selected");
        AddInfo($"Warning on: {DescribeOutputs(_settings)}");
        if (icpLine != null) AddInfo(icpLine);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add(new ToolStripMenuItem("Settings…", null, (_, _) => OpenSettings()) { Font = new Font(_menu.Font, FontStyle.Bold) });
        _menu.Items.Add("Show test warning (5 s)", null, (_, _) => _controller.ShowTest(TimeSpan.FromSeconds(5)));
        _menu.Items.Add("Open log", null, (_, _) => OpenFile(Log.FilePath));
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("Exit", null, (_, _) => ExitThread());
    }

    private void AddInfo(string text) => _menu.Items.Add(new ToolStripMenuItem(text) { Enabled = false });

    private static void OpenFile(string path)
    {
        try
        {
            if (File.Exists(path)) Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    protected override void ExitThreadCore()
    {
        Log.Info("Exiting.");
        _tray.Visible = false;
        _settingsForm?.Close();
        _monitor?.Dispose();
        _controller.Dispose();
        _icp?.Dispose(); // blanks the DED
        _tray.Dispose();
        _invoker.Dispose();
        base.ExitThreadCore();
    }

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr handle);

    private static Icon MakeIcon(Color color)
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using var brush = new SolidBrush(color);
            using var pen = new Pen(Color.Black, 2);
            g.FillEllipse(brush, 2, 2, 28, 28);
            g.DrawEllipse(pen, 2, 2, 28, 28);
            // simple microphone glyph
            using var white = new SolidBrush(Color.White);
            using var whitePen = new Pen(Color.White, 2);
            g.FillRectangle(white, 13, 7, 6, 11);
            g.FillEllipse(white, 13, 4, 6, 6);
            g.FillEllipse(white, 13, 15, 6, 6);
            g.DrawArc(whitePen, 9, 10, 14, 12, 0, 180);
            g.DrawLine(whitePen, 16, 22, 16, 26);
        }
        var h = bmp.GetHicon();
        var icon = (Icon)Icon.FromHandle(h).Clone();
        DestroyIcon(h);
        return icon;
    }
}
