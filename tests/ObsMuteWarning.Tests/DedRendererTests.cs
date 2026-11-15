using ObsMuteWarning.Icp;

namespace ObsMuteWarning.Tests;

public class DedRendererTests
{
    [Fact]
    public void BigMute_FillsCentredBoxAndLeavesHeaderZero()
    {
        var frame = DedRenderer.BigMute(inverted: false);

        Assert.Equal(1629, frame.Length);
        Assert.All(frame.Take(DedRenderer.HeaderSize), b => Assert.Equal(0, b));

        int minX = int.MaxValue, maxX = -1, minY = int.MaxValue, maxY = -1;
        for (int y = 0; y < DedRenderer.Height; y++)
            for (int x = 0; x < DedRenderer.Width; x++)
                if (DedRenderer.GetPixel(frame, x, y))
                {
                    minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
                }

        // 4 letters x 5 cells + 3 gaps = 23 cells at 8 px => 184 x 56, centred on 200 x 65.
        Assert.Equal((8, 191, 4, 59), (minX, maxX, minY, maxY));
        // Top-left pixel of 'M' is bit 0 of the second byte in row 4.
        Assert.Equal(1, frame[DedRenderer.HeaderSize + 4 * DedRenderer.BytesPerRow + 1] & 1);
    }

    [Fact]
    public void InverseFrame_IsPixelComplementOfNormal()
    {
        var normal = DedRenderer.BigMute(inverted: false);
        var inverse = DedRenderer.BigMute(inverted: true);

        Assert.All(inverse.Take(DedRenderer.HeaderSize), b => Assert.Equal(0, b));
        for (int i = DedRenderer.HeaderSize; i < normal.Length; i++)
            Assert.Equal((byte)~normal[i], inverse[i]);
    }
}
