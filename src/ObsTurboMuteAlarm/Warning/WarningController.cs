namespace ObsTurboMuteAlarm.Warning;

/// <summary>A place where the flashing warning can appear (a monitor popup or the ICP DED).</summary>
internal interface IWarningOutput : IDisposable
{
    /// <summary>Shows one flash phase. <paramref name="mutedSources"/> is empty for a test warning.</summary>
    void Show(bool alternatePhase, IReadOnlyList<string> mutedSources);

    void Hide();
}

/// <summary>
/// Decides when the warning is visible (any watched source muted, or a test is running)
/// and drives every output with a shared flash timer so they blink in sync. Thread-safe.
/// </summary>
internal sealed class WarningController : IDisposable
{
    private readonly object _lock = new();
    private readonly System.Threading.Timer _timer;
    private List<IWarningOutput> _outputs = new();
    private IReadOnlyList<string> _muted = Array.Empty<string>();
    private DateTime _testUntil = DateTime.MinValue;
    private int _intervalMs;
    private bool _showing;
    private bool _alternate;
    private bool _disposed;

    public WarningController(int flashIntervalMs)
    {
        _intervalMs = flashIntervalMs;
        _timer = new System.Threading.Timer(_ => Tick());
    }

    public bool IsShowing { get { lock (_lock) return _showing; } }

    /// <summary>Replaces the outputs (old ones are hidden and disposed).</summary>
    public void Configure(IEnumerable<IWarningOutput> outputs, int flashIntervalMs)
    {
        List<IWarningOutput> old;
        lock (_lock)
        {
            old = _outputs;
            _outputs = outputs.ToList();
            _intervalMs = flashIntervalMs;
            foreach (var o in old) Safe(() => { o.Hide(); o.Dispose(); });
            if (_showing)
            {
                foreach (var o in _outputs) Safe(() => o.Show(_alternate, _muted));
                _timer.Change(_intervalMs, _intervalMs);
            }
        }
    }

    /// <summary>Names of the watched sources that are currently muted (empty = none).</summary>
    public void SetMutedSources(IReadOnlyList<string> muted)
    {
        lock (_lock)
        {
            bool namesChanged = !_muted.SequenceEqual(muted);
            _muted = muted.ToArray();
            if (!Update() && _showing && namesChanged)
                foreach (var o in _outputs) Safe(() => o.Show(_alternate, _muted));
        }
    }

    /// <summary>Shows the warning for the given time regardless of OBS state.</summary>
    public void ShowTest(TimeSpan duration)
    {
        lock (_lock)
        {
            _testUntil = DateTime.UtcNow + duration;
            Update();
        }
    }

    private void Tick()
    {
        lock (_lock)
        {
            if (_disposed || !_showing) return;
            if (Update()) return;
            _alternate = !_alternate;
            foreach (var o in _outputs) Safe(() => o.Show(_alternate, _muted));
        }
    }

    private bool ShouldShow() => _muted.Count > 0 || DateTime.UtcNow < _testUntil;

    /// <summary>Starts or stops the warning if needed. Returns true if it changed state.</summary>
    private bool Update()
    {
        if (_disposed) return false;
        bool show = ShouldShow();
        if (show == _showing) return false;
        _showing = show;
        if (show)
        {
            _alternate = false;
            foreach (var o in _outputs) Safe(() => o.Show(false, _muted));
            _timer.Change(_intervalMs, _intervalMs);
        }
        else
        {
            _timer.Change(Timeout.Infinite, Timeout.Infinite);
            foreach (var o in _outputs) Safe(o.Hide);
        }
        return true;
    }

    private static void Safe(Action a)
    {
        try { a(); }
        catch (Exception ex) { Log.Info($"Warning output error: {ex.Message}"); }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Dispose();
            foreach (var o in _outputs) Safe(() => { o.Hide(); o.Dispose(); });
            _outputs = new();
        }
    }
}
