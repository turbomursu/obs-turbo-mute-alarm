namespace ObsMuteWarning.Obs;

internal sealed record SourceStatus(string DisplayName, bool Found, bool? Muted);

internal sealed record ObsStatus(bool Connected, string? Error, IReadOnlyList<SourceStatus> Sources)
{
    public bool AnyMuted => Sources.Any(s => s.Muted == true);
}

internal readonly record struct ObsInput(string Name, string Uuid);

/// <summary>
/// Pure state for the watched sources: maps each configured source to an actual OBS input
/// (UUID first, then name) and tracks its mute state. Not thread-safe; callers lock.
/// </summary>
internal sealed class MuteStateTracker
{
    private sealed class Entry
    {
        public required WatchedSource Source { get; init; }
        public ObsInput? Resolved { get; set; }
        public bool? Muted { get; set; }
        public int EventSeq { get; set; }
    }

    private readonly List<Entry> _entries;

    public MuteStateTracker(IEnumerable<WatchedSource> sources)
    {
        _entries = sources.Select(s => new Entry { Source = s }).ToList();
    }

    public int Count => _entries.Count;

    public bool AnyMuted => _entries.Any(e => e.Muted == true);

    public ObsInput? ResolvedInput(int index) => _entries[index].Resolved;

    /// <summary>Re-binds every watched source to the current OBS inputs; clears mute states.</summary>
    public void Resolve(IReadOnlyCollection<ObsInput> inputs)
    {
        foreach (var e in _entries)
        {
            ObsInput? match = null;
            if (!string.IsNullOrEmpty(e.Source.Uuid))
                match = inputs.Where(i => string.Equals(i.Uuid, e.Source.Uuid, StringComparison.OrdinalIgnoreCase))
                              .Select(i => (ObsInput?)i).FirstOrDefault();
            if (match == null && !string.IsNullOrEmpty(e.Source.Name))
                match = inputs.Where(i => string.Equals(i.Name, e.Source.Name, StringComparison.Ordinal))
                              .Select(i => (ObsInput?)i).FirstOrDefault();
            e.Resolved = match;
            e.Muted = null;
        }
    }

    public void SetMuted(int index, bool? muted) => _entries[index].Muted = muted;

    /// <summary>Counter bumped by every mute event for the source; used to discard stale query results.</summary>
    public int EventSeq(int index) => _entries[index].EventSeq;

    /// <summary>Applies a queried mute state unless a mute event arrived after the query was sent.</summary>
    public void SetQueriedMuted(int index, bool muted, int eventSeqAtQuery)
    {
        var e = _entries[index];
        if (e.EventSeq == eventSeqAtQuery) e.Muted = muted;
    }

    /// <summary>Applies an InputMuteStateChanged event. Returns true if a watched source was affected.</summary>
    public bool ApplyMuteEvent(string? uuid, string? name, bool muted)
    {
        bool hit = false;
        foreach (var e in _entries)
        {
            if (e.Resolved is not { } r) continue;
            bool matches = !string.IsNullOrEmpty(uuid)
                ? string.Equals(r.Uuid, uuid, StringComparison.OrdinalIgnoreCase)
                : string.Equals(r.Name, name, StringComparison.Ordinal);
            if (!matches) continue;
            e.Muted = muted;
            e.EventSeq++;
            hit = true;
        }
        return hit;
    }

    public void Reset()
    {
        foreach (var e in _entries)
        {
            e.Resolved = null;
            e.Muted = null;
        }
    }

    public IReadOnlyList<SourceStatus> Snapshot() =>
        _entries.Select(e => new SourceStatus(e.Resolved?.Name ?? e.Source.ToString(), e.Resolved != null, e.Muted))
                .ToList();
}
