using System.Text;

namespace ObsMuteWarning.Icp;

/// <summary>
/// Builds raw DED frames for the ICP LCD: a 4-byte header followed by
/// 65 rows x 25 bytes (200 pixels). Within each byte bit 0 is the leftmost pixel;
/// a set bit is a lit pixel.
/// </summary>
internal static class DedRenderer
{
    public const int Width = 200;
    public const int Height = 65;
    public const int HeaderSize = 4;
    public const int BytesPerRow = Width / 8;
    public const int FrameSize = HeaderSize + Height * BytesPerRow;

    private const int GlyphW = 5;
    private const int GlyphH = 7;

    private static readonly Dictionary<char, string[]> Glyphs = new()
    {
        ['M'] = new[] { "#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#" },
        ['U'] = new[] { "#...#", "#...#", "#...#", "#...#", "#...#", "#...#", ".###." },
        ['T'] = new[] { "#####", "..#..", "..#..", "..#..", "..#..", "..#..", "..#.." },
        ['E'] = new[] { "#####", "#....", "#....", "####.", "#....", "#....", "#####" },
    };

    public static byte[] Blank() => new byte[FrameSize];

    /// <summary>Renders "MUTE" as block letters scaled to fill the display, centred.</summary>
    public static byte[] BigMute(bool inverted)
    {
        var frame = RenderBigText("MUTE");
        if (inverted) Invert(frame);
        return frame;
    }

    internal static byte[] RenderBigText(string text)
    {
        var frame = Blank();
        int n = text.Length;
        int cells = n * GlyphW + (n - 1); // one empty cell between letters
        int scale = Math.Min(Width / cells, Height / GlyphH);
        int x0 = (Width - cells * scale) / 2;
        int y0 = (Height - GlyphH * scale) / 2;

        for (int i = 0; i < n; i++)
        {
            var glyph = Glyphs[char.ToUpperInvariant(text[i])];
            int gx = x0 + i * (GlyphW + 1) * scale;
            for (int r = 0; r < GlyphH; r++)
                for (int c = 0; c < GlyphW; c++)
                    if (glyph[r][c] == '#')
                        FillRect(frame, gx + c * scale, y0 + r * scale, scale, scale);
        }
        return frame;
    }

    public static bool GetPixel(byte[] frame, int x, int y) =>
        (frame[HeaderSize + y * BytesPerRow + x / 8] & (1 << (x % 8))) != 0;

    private static void SetPixel(byte[] frame, int x, int y) =>
        frame[HeaderSize + y * BytesPerRow + x / 8] |= (byte)(1 << (x % 8));

    private static void FillRect(byte[] frame, int x, int y, int w, int h)
    {
        for (int yy = y; yy < y + h; yy++)
            for (int xx = x; xx < x + w; xx++)
                SetPixel(frame, xx, yy);
    }

    private static void Invert(byte[] frame)
    {
        for (int i = HeaderSize; i < frame.Length; i++)
            frame[i] = (byte)~frame[i];
    }

    public static string ToAscii(byte[] frame)
    {
        var sb = new StringBuilder((Width + 2) * Height);
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
                sb.Append(GetPixel(frame, x, y) ? '#' : '.');
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
