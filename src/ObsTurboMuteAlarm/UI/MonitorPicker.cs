using System.Drawing.Drawing2D;
using System.Text.RegularExpressions;

namespace ObsTurboMuteAlarm.UI;

/// <summary>
/// Shows the monitors in their real Windows arrangement. Click a monitor (or use the arrow keys and Space)
/// to select or deselect it. Selection is stored as <see cref="Screen.DeviceName"/> strings.
/// </summary>
internal sealed class MonitorPicker : Control
{
    private static readonly Color Selected = Color.FromArgb(220, 0, 0);
    private static readonly Color Unselected = Color.FromArgb(232, 232, 232);

    private readonly HashSet<string> _selected = new(StringComparer.OrdinalIgnoreCase);
    private Screen[] _screens = Array.Empty<Screen>();
    private Rectangle[] _rects = Array.Empty<Rectangle>();
    private int _hover = -1;
    private int _focus;

    public event EventHandler? SelectionChanged;

    public MonitorPicker()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable | ControlStyles.UserPaint, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleName = "Monitors";
        AccessibleRole = AccessibleRole.List;
        RefreshScreens();
    }

    /// <summary>Selected monitors, including saved ones that are not connected right now.</summary>
    public IReadOnlyCollection<string> SelectedDevices => _selected;

    public void SetSelected(IEnumerable<string> deviceNames)
    {
        _selected.Clear();
        _selected.UnionWith(deviceNames);
        Invalidate();
    }

    /// <summary>Drops selected monitors that are not connected.</summary>
    public void ForgetDisconnected()
    {
        if (_selected.RemoveWhere(d => !_screens.Any(s => Same(s, d))) > 0)
        {
            Invalidate();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public IEnumerable<string> DisconnectedSelection => _selected.Where(d => !_screens.Any(s => Same(s, d)));

    public void RefreshScreens()
    {
        _screens = Screen.AllScreens.OrderBy(s => MonitorNumber(s.DeviceName)).ToArray();
        _focus = Math.Clamp(_focus, 0, Math.Max(0, _screens.Length - 1));
        _hover = -1;
        UpdateLayout();
        Invalidate();
    }

    internal static int MonitorNumber(string deviceName) =>
        Regex.Match(deviceName, @"(\d+)\s*$") is { Success: true } m ? int.Parse(m.Groups[1].Value) : 0;

    /// <summary>Scales the monitor bounds (virtual-desktop coordinates) to fit <paramref name="client"/>, keeping the arrangement.</summary>
    internal static Rectangle[] ComputeLayout(IReadOnlyList<Rectangle> bounds, Size client, int margin = 8, int gap = 6)
    {
        if (bounds.Count == 0) return Array.Empty<Rectangle>();
        var union = bounds.Aggregate(Rectangle.Union);
        double availW = Math.Max(1, client.Width - 2 * margin), availH = Math.Max(1, client.Height - 2 * margin);
        double scale = Math.Min(availW / union.Width, availH / union.Height);
        double offX = margin + (availW - union.Width * scale) / 2, offY = margin + (availH - union.Height * scale) / 2;
        return bounds.Select(b =>
        {
            int left = (int)Math.Round(offX + (b.Left - union.Left) * scale), right = (int)Math.Round(offX + (b.Right - union.Left) * scale);
            int top = (int)Math.Round(offY + (b.Top - union.Top) * scale), bottom = (int)Math.Round(offY + (b.Bottom - union.Top) * scale);
            var r = Rectangle.FromLTRB(left, top, right, bottom);
            r.Inflate(-gap / 2, -gap / 2);
            return r;
        }).ToArray();
    }

    private static bool Same(Screen s, string device) => string.Equals(s.DeviceName, device, StringComparison.OrdinalIgnoreCase);

    private void UpdateLayout() => _rects = ComputeLayout(_screens.Select(s => s.Bounds).ToList(), ClientSize);

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateLayout();
    }

    private void Toggle(int index)
    {
        if (index < 0 || index >= _screens.Length) return;
        string device = _screens[index].DeviceName;
        if (!_selected.Remove(device)) _selected.Add(device);
        _focus = index;
        Invalidate();
        SelectionChanged?.Invoke(this, EventArgs.Empty);
    }

    private int HitTest(Point p) => Array.FindIndex(_rects, r => r.Contains(p));

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        int hit = HitTest(e.Location);
        if (hit != _hover) { _hover = hit; Invalidate(); }
        Cursor = hit >= 0 ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hover != -1) { _hover = -1; Invalidate(); }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        Focus();
        if (e.Button == MouseButtons.Left) Toggle(HitTest(e.Location));
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (_screens.Length == 0) return;
        switch (e.KeyCode)
        {
            case Keys.Left or Keys.Up: _focus = (_focus + _screens.Length - 1) % _screens.Length; Invalidate(); break;
            case Keys.Right or Keys.Down: _focus = (_focus + 1) % _screens.Length; Invalidate(); break;
            case Keys.Space or Keys.Enter: Toggle(_focus); e.Handled = true; break;
        }
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(SystemColors.Window);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        for (int i = 0; i < _screens.Length; i++)
        {
            var r = _rects[i];
            var screen = _screens[i];
            bool on = _selected.Contains(screen.DeviceName);
            Color fore = on ? Color.White : Color.FromArgb(40, 40, 40);

            using (var fill = new SolidBrush(on ? Selected : Unselected))
                g.FillRectangle(fill, r);
            using (var border = new Pen(i == _hover ? SystemColors.Highlight : Color.FromArgb(90, 90, 90), i == _hover ? 3 : 1))
                g.DrawRectangle(border, r);
            if (Focused && i == _focus)
            {
                var fr = r;
                fr.Inflate(-4, -4);
                ControlPaint.DrawFocusRectangle(g, fr, fore, on ? Selected : Unselected);
            }

            float numberSize = Math.Max(8, Math.Min(r.Height * 0.38f, r.Width * 0.3f));
            float smallSize = Math.Max(7, Math.Min(11, r.Height * 0.11f));
            using var numberFont = new Font(Font.FontFamily, numberSize, FontStyle.Bold, GraphicsUnit.Pixel);
            using var smallFont = new Font(Font.FontFamily, smallSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var brush = new SolidBrush(fore);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };

            var numberRect = new RectangleF(r.X, r.Y, r.Width, r.Height * 0.62f);
            g.DrawString(MonitorNumber(screen.DeviceName).ToString(), numberFont, brush, numberRect, center);

            string info = $"{screen.Bounds.Width}×{screen.Bounds.Height}{(screen.Primary ? "\nprimary" : "")}";
            if (on) info = "WARNING HERE\n" + info;
            var infoRect = new RectangleF(r.X + 2, r.Y + r.Height * 0.55f, r.Width - 4, r.Height * 0.45f);
            g.DrawString(info, smallFont, brush, infoRect, center);
        }
    }
}
