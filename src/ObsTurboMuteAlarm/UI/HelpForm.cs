using System.Diagnostics;
using System.Reflection;

namespace ObsTurboMuteAlarm.UI;

/// <summary>Help window with quick instructions, the Mursu-soft logo and license info.</summary>
internal sealed class HelpForm : Form
{
    private const string AppName = "OBS Turbo Mute Alarm";
    internal const string RepoUrl = "https://github.com/turbomursu/obs-turbo-mute-alarm";

    private static HelpForm? _instance;

    /// <summary>Opens the help window, or brings the open one to the front.</summary>
    public static void ShowHelp()
    {
        if (_instance is { IsDisposed: false })
        {
            if (_instance.WindowState == FormWindowState.Minimized) _instance.WindowState = FormWindowState.Normal;
            _instance.Activate();
            return;
        }
        _instance = new HelpForm();
        _instance.FormClosed += (_, _) => _instance = null;
        _instance.Show();
        _instance.Activate();
    }

    /// <summary>Lets F1 open help from any window.</summary>
    public static void AttachF1(Form form)
    {
        form.KeyPreview = true;
        form.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.F1) return;
            e.Handled = true;
            ShowHelp();
        };
    }

    private HelpForm()
    {
        Text = $"{AppName} – Help";
        Icon = Program.AppIcon;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(8);
        BackColor = SystemColors.Window;

        var root = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, AutoSize = true };

        var logo = new PictureBox
        {
            Image = LoadLogo(),
            SizeMode = PictureBoxSizeMode.Zoom,
            Size = new Size(240, 240),
            Margin = new Padding(3, 3, 12, 3),
        };
        root.Controls.Add(logo, 0, 0);

        var text = new RichTextBox
        {
            ReadOnly = true,
            BorderStyle = BorderStyle.None,
            BackColor = SystemColors.Window,
            ScrollBars = RichTextBoxScrollBars.Vertical,
            Size = new Size(480, 560),
            TabStop = false,
            DetectUrls = false,
        };
        WriteInstructions(text);
        root.Controls.Add(text, 1, 0);

        var bottom = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        string version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "";
        var about = new LinkLabel
        {
            Text = $"{AppName} {version} by Mursu-soft · Free and open source (The Unlicense) · {RepoUrl}",
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(3, 7, 3, 3),
        };
        about.Links.Add(about.Text.IndexOf(RepoUrl, StringComparison.Ordinal), RepoUrl.Length, RepoUrl);
        about.LinkClicked += (_, e) => OpenUrl((string)e.Link!.LinkData!);
        bottom.Controls.Add(about);
        var close = new Button { Text = "Close", DialogResult = DialogResult.Cancel, AutoSize = true, UseVisualStyleBackColor = true };
        close.Click += (_, _) => Close();
        bottom.Controls.Add(close);
        root.Controls.Add(bottom, 0, 1);
        root.SetColumnSpan(bottom, 2);

        Controls.Add(root);
        AcceptButton = close;
        CancelButton = close;
        FormClosed += (_, _) => logo.Image?.Dispose();
    }

    private static Image? LoadLogo()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ObsTurboMuteAlarm.Resources.mursu-soft.png");
        return stream == null ? null : new Bitmap(stream);
    }

    private static void WriteInstructions(RichTextBox box)
    {
        var baseFont = SystemFonts.MessageBoxFont ?? DefaultFont;
        var title = new Font(baseFont.FontFamily, baseFont.Size + 5, FontStyle.Bold);
        var heading = new Font(baseFont.FontFamily, baseFont.Size + 1.5f, FontStyle.Bold);
        var body = new Font(baseFont.FontFamily, baseFont.Size + 0.5f);

        void Write(string s, Font f, int hangingIndent = 0)
        {
            box.SelectionStart = box.TextLength;
            box.SelectionFont = f;
            box.SelectionHangingIndent = hangingIndent;
            box.AppendText(s);
        }
        const int list = 18;

        Write($"{AppName}\n", title);
        Write("Never stream or record in silence again. When an OBS audio input you choose is muted, " +
              "a big flashing MUTED warning appears on your monitor and/or the WinWing ICP display.\n\n", body);
        Write("100% vibe coded in Claude Opus 5.5. Use entirely at your own risk.\n\n",
              new Font(body, FontStyle.Bold | FontStyle.Italic));

        Write("Quick start\n", heading);
        Write("1.  In OBS, open Tools → WebSocket Server Settings and tick Enable WebSocket server.\n" +
              "2.  Click the tray icon to open Choose inputs and displays.\n" +
              "3.  Tick the audio inputs to watch, for example your microphone.\n" +
              "4.  Click the monitors where the warning should pop up (red = selected). " +
              "If you installed ICP support, tick WinWing ViperAce ICP to flash it on the ICP too.\n" +
              "5.  Click Apply & test (5 s) to try it, then Save.\n", body, list);
        Write("\n", body);

        Write("Tray icon\n", heading);
        Write("Green = all watched inputs live.  Red = something is muted.  Grey = OBS not connected.\n" +
              "Left-click: choose inputs and displays.  Right-click: menu with Settings, test and Exit.\n\n", body);

        Write("Troubleshooting\n", heading);
        Write("•  OBS: disconnected – check that OBS is running and the WebSocket server is enabled. " +
              "If OBS is on another PC or uses another port or password, set it in Settings.\n" +
              "•  ICP: not found – plug in the ICP. SimAppPro can keep running.\n" +
              "•  ICP support not installed – run the installer again and tick WinWing ViperAce ICP support.\n" +
              "•  Input not listed – tick Show all sources with audio, or click Refresh.\n" +
              "•  More details are in the log (tray menu → Open log).\n", body, list);
        Write("\n", body);

        Write("Uninstall\n", heading);
        Write("Windows Settings → Apps → Installed apps → OBS Turbo Mute Alarm → Uninstall.\n\n", body);

        Write("Press F1 in any window to open this help.", body);
        box.SelectionStart = 0;
    }

    private static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show(ex.Message, AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }
}
