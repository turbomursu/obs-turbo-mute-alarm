using ObsMuteWarning.UI;

namespace ObsMuteWarning.Tests;

public class MonitorPickerTests
{
    [Fact]
    public void LayoutKeepsArrangementAndFits()
    {
        // 1080p primary, a second 1080p to its left, and a portrait monitor above-right.
        var bounds = new[]
        {
            new Rectangle(0, 0, 1920, 1080),
            new Rectangle(-1920, 0, 1920, 1080),
            new Rectangle(1920, -400, 768, 1024),
        };
        var client = new Size(440, 220);

        var r = MonitorPicker.ComputeLayout(bounds, client);

        Assert.All(r, x => Assert.True(new Rectangle(Point.Empty, client).Contains(x), x.ToString()));
        Assert.True(r[1].Right <= r[0].Left);   // left monitor is drawn to the left
        Assert.True(r[2].Left >= r[0].Right);   // right monitor is drawn to the right
        Assert.True(r[2].Top < r[0].Top);       // and higher up
        Assert.True(r[2].Height > r[2].Width);  // portrait stays portrait
        for (int i = 0; i < r.Length; i++)
            for (int j = i + 1; j < r.Length; j++)
                Assert.False(r[i].IntersectsWith(r[j]), $"{i} overlaps {j}");
    }
}
