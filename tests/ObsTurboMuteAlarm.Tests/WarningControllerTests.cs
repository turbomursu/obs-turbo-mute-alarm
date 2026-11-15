using ObsTurboMuteAlarm.Warning;

namespace ObsTurboMuteAlarm.Tests;

public class WarningControllerTests
{
    private sealed class FakeOutput : IWarningOutput
    {
        public readonly List<string> Calls = new();
        public void Show(bool alternatePhase, IReadOnlyList<string> mutedSources) =>
            Add($"show:{alternatePhase}:{string.Join(",", mutedSources)}");
        public void Hide() => Add("hide");
        public void Dispose() => Add("dispose");
        private void Add(string s) { lock (Calls) Calls.Add(s); }
        public string[] Snapshot() { lock (Calls) return Calls.ToArray(); }
    }

    [Fact]
    public void FlashesAllOutputsInSyncWhileMutedAndHidesWhenLive()
    {
        var a = new FakeOutput();
        var b = new FakeOutput();
        using var c = new WarningController(50);
        c.Configure(new[] { a, b }, 50);

        c.SetMutedSources(new[] { "Mic" });
        Assert.True(c.IsShowing);
        Thread.Sleep(180);
        c.SetMutedSources(Array.Empty<string>());
        Assert.False(c.IsShowing);

        var calls = a.Snapshot();
        Assert.Equal("show:False:Mic", calls[0]);
        Assert.Contains("show:True:Mic", calls);
        Assert.Equal("hide", calls[^1]);
        Assert.Equal(calls, b.Snapshot());
    }

    [Fact]
    public void ReconfiguringWhileShowingHidesOldOutputsAndShowsNewOnes()
    {
        var old = new FakeOutput();
        var replacement = new FakeOutput();
        using var c = new WarningController(10_000);
        c.Configure(new[] { old }, 10_000);
        c.SetMutedSources(new[] { "Mic" });

        c.Configure(new[] { replacement }, 10_000);

        Assert.Equal(new[] { "show:False:Mic", "hide", "dispose" }, old.Snapshot());
        Assert.Equal(new[] { "show:False:Mic" }, replacement.Snapshot());
    }

    [Fact]
    public void TestWarningEndsByItself()
    {
        var o = new FakeOutput();
        using var c = new WarningController(40);
        c.Configure(new[] { o }, 40);

        c.ShowTest(TimeSpan.FromMilliseconds(150));
        Assert.True(c.IsShowing);
        Assert.Equal("show:False:", o.Snapshot()[0]);
        Thread.Sleep(400);

        Assert.False(c.IsShowing);
        Assert.Equal("hide", o.Snapshot()[^1]);
    }

    [Fact]
    public void PopupStaysInsideTheMonitor()
    {
        var area = new Rectangle(-1920, 0, 1920, 1040); // a monitor left of the primary
        foreach (var position in Enum.GetValues<PopupPosition>())
        foreach (var size in Enum.GetValues<PopupSize>())
        {
            var r = WarningPopupForm.ComputeBounds(area, position, size);
            Assert.True(area.Contains(r), $"{position}/{size}: {r}");
            Assert.True(r.Width > r.Height);
        }
    }
}
