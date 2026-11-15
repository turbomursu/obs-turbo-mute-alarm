using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace ObsMuteWarning.Warning;

internal sealed record PopupStyle(string Text, PopupPosition Position, PopupSize Size, int OpacityPercent, bool ClickThrough);

/// <summary>
/// Borderless always-on-top warning window. It never takes focus, stays out of Alt+Tab
/// and (optionally) lets mouse clicks pass through to the window underneath.
/// </summary>
internal sealed class WarningPopupForm : Form
{
    private static readonly Color Red = Color.FromArgb(220, 0, 0);
    private static readonly Color Dark = Color.FromArgb(24, 24, 24);

    private readonly PopupStyle _style;
    private bool _alternate;
    private string _subtitle = "";

    public WarningPopupForm(PopupStyle style)
    {
        _style = style;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = true;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        Text = "OBS Mute Warning";
        Opacity = style.OpacityPercent / 100.0;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            if (_style?.ClickThrough == true) cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_LAYERED;
            return cp;
        }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // A layered window stays invisible until its alpha is set; WinForms only does that when Opacity < 1.
        if (_style.ClickThrough && _style.OpacityPercent >= 100)
            SetLayeredWindowAttributes(Handle, 0, 255, LWA_ALPHA);
    }

    public void ShowPhase(Screen screen, bool alternate, IReadOnlyList<string> mutedSources)
    {
        _alternate = alternate;
        _subtitle = mutedSources.Count == 0 ? "Test warning" : string.Join(", ", mutedSources);
        var bounds = ComputeBounds(screen.WorkingArea, _style.Position, _style.Size);
        if (Bounds != bounds) Bounds = bounds;
        if (!Visible) Show();
        // Re-assert topmost on every flash in case another topmost window covered it.
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Invalidate();
    }

    /// <summary>Popup rectangle inside a monitor's working area.</summary>
    internal static Rectangle ComputeBounds(Rectangle area, PopupPosition position, PopupSize size)
    {
        double fraction = size switch { PopupSize.Small => 0.22, PopupSize.Large => 0.45, _ => 0.32 };
        int width = Math.Max(240, (int)(Math.Max(area.Width, area.Height * 1.2) * fraction));
        width = Math.Min(width, area.Width);
        int height = Math.Min(area.Height, (int)(width * 0.3));
        int margin = (int)(Math.Min(area.Width, area.Height) * 0.03);

        int col = (int)position % 3, row = (int)position / 3;
        int x = col switch
        {
            0 => area.Left + margin,
            1 => area.Left + (area.Width - width) / 2,
            _ => area.Right - margin - width,
        };
        int y = row switch
        {
            0 => area.Top + margin,
            1 => area.Top + (area.Height - height) / 2,
            _ => area.Bottom - margin - height,
        };
        return new Rectangle(x, y, width, height);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;

        Color back = _alternate ? Dark : Red;
        Color fore = _alternate ? Red : Color.White;
        g.Clear(back);

        int border = Math.Max(4, Height / 20);
        using (var pen = new Pen(_alternate ? Red : Color.White, border) { Alignment = PenAlignment.Inset })
            g.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);

        var inner = Rectangle.Inflate(ClientRectangle, -border * 2, -border);
        var titleRect = new Rectangle(inner.X, inner.Y, inner.Width, (int)(inner.Height * 0.72));
        var subRect = new Rectangle(inner.X, titleRect.Bottom, inner.Width, inner.Bottom - titleRect.Bottom);

        using var brush = new SolidBrush(fore);
        using var center = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap,
        };

        using (var titleFont = FitFont(g, _style.Text, titleRect, FontStyle.Bold))
            g.DrawString(_style.Text, titleFont, brush, titleRect, center);

        if (subRect.Height > 8)
        {
            using var subFont = new Font("Segoe UI", Math.Max(8f, subRect.Height * 0.62f), FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString(_subtitle, subFont, brush, subRect, center);
        }
    }

    private static Font FitFont(Graphics g, string text, Rectangle rect, FontStyle style)
    {
        float px = rect.Height * 0.85f;
        while (true)
        {
            var font = new Font("Segoe UI", px, style, GraphicsUnit.Pixel);
            var size = g.MeasureString(text, font);
            if (px <= 8 || (size.Width <= rect.Width && size.Height <= rect.Height * 1.25f)) return font;
            font.Dispose();
            px *= 0.92f;
        }
    }

    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const uint LWA_ALPHA = 0x2;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    private static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
}

/// <summary>Shows the warning popup on one monitor. All window work is marshalled to the UI thread.</summary>
internal sealed class PopupOutput : IWarningOutput
{
    private readonly Control _ui;
    private readonly string _deviceName;
    private readonly PopupStyle _style;
    private WarningPopupForm? _form; // UI thread only
    private bool _disposed;          // UI thread only

    public PopupOutput(Control uiInvoker, string monitorDeviceName, PopupStyle style)
    {
        _ui = uiInvoker;
        _deviceName = monitorDeviceName;
        _style = style;
    }

    public void Show(bool alternatePhase, IReadOnlyList<string> mutedSources)
    {
        var names = mutedSources.ToArray();
        Post(() =>
        {
            if (_disposed) return;
            _form ??= new WarningPopupForm(_style);
            _form.ShowPhase(FindScreen(_deviceName), alternatePhase, names);
        });
    }

    public void Hide() => Post(() => _form?.Hide());

    public void Dispose() => Post(() =>
    {
        _disposed = true;
        _form?.Dispose();
        _form = null;
    });

    /// <summary>The selected monitor, or the primary one if it is not connected right now.</summary>
    internal static Screen FindScreen(string deviceName) =>
        Screen.AllScreens.FirstOrDefault(s => string.Equals(s.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase))
        ?? Screen.PrimaryScreen!;

    private void Post(Action action)
    {
        if (_ui.IsDisposed) return;
        try { _ui.BeginInvoke(action); }
        catch (InvalidOperationException) { } // UI shutting down
    }
}
