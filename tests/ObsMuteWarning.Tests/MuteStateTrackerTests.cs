using ObsMuteWarning.Obs;

namespace ObsMuteWarning.Tests;

public class MuteStateTrackerTests
{
    private static MuteStateTracker Create() => new(new[]
    {
        new WatchedSource { Name = "Desk mic", Uuid = "uuid-mic" },
        new WatchedSource { Name = "Game /Äänet", Uuid = "uuid-gone" },
    });

    [Fact]
    public void Resolve_PrefersUuidAndFallsBackToName()
    {
        var t = Create();
        t.Resolve(new[]
        {
            new ObsInput("Renamed mic", "UUID-MIC"),
            new ObsInput("Game /Äänet", "uuid-new"),
            new ObsInput("Desk mic", "uuid-other"),
        });

        Assert.Equal(new ObsInput("Renamed mic", "UUID-MIC"), t.ResolvedInput(0));
        Assert.Equal(new ObsInput("Game /Äänet", "uuid-new"), t.ResolvedInput(1));
    }

    [Fact]
    public void MuteEvents_AffectOnlyWatchedSources_AndAnyMutedTriggers()
    {
        var t = Create();
        t.Resolve(new[] { new ObsInput("Desk mic", "uuid-mic"), new ObsInput("Game /Äänet", "uuid-game") });
        t.SetMuted(0, false);
        t.SetMuted(1, false);

        Assert.False(t.ApplyMuteEvent("uuid-unrelated", "Desktop Audio", true));
        Assert.False(t.AnyMuted);

        Assert.True(t.ApplyMuteEvent("uuid-game", "Game /Äänet", true));
        Assert.True(t.AnyMuted);

        Assert.True(t.ApplyMuteEvent(null, "Game /Äänet", false));
        Assert.False(t.AnyMuted);
    }

    [Fact]
    public void QueryResult_OlderThanMuteEvent_IsIgnored()
    {
        var t = Create();
        t.Resolve(new[] { new ObsInput("Desk mic", "uuid-mic") });
        int seq = t.EventSeq(0);

        t.ApplyMuteEvent("uuid-mic", "Desk mic", true);
        t.SetQueriedMuted(0, false, seq);

        Assert.True(t.AnyMuted);
    }

    [Fact]
    public void UnresolvedSource_IsReportedNotFoundAndNeverMuted()
    {
        var t = Create();
        t.Resolve(new[] { new ObsInput("Desk mic", "uuid-mic") });

        Assert.False(t.ApplyMuteEvent("uuid-gone", "Game /Äänet", true));
        var snap = t.Snapshot();
        Assert.True(snap[0].Found);
        Assert.False(snap[1].Found);
        Assert.False(t.AnyMuted);
    }
}
