namespace FrameViewAnalyzer.App.Busy;

/// <summary>
/// Per-window busy state: one instance belongs to exactly one Window and
/// tracks every in-flight operation of that window. Operations open scopes
/// with <see cref="Begin"/> (or the <see cref="RunAsync(string, Func{Task})"/>
/// helpers); the window is busy until every scope has ended, so nested and
/// overlapping operations can never return the window to READY too early.
///
/// Two levels of busy exist:
/// <list type="bullet">
/// <item><see cref="IsBusy"/> — logical, flips immediately. Commands use it
/// to block conflicting actions.</item>
/// <item><see cref="IsBusyVisible"/> — visual, flips only after an operation
/// outlives the presentation threshold (<see cref="DefaultDisplayDelay"/>),
/// so very fast operations never flash the dimmed overlay. The animated
/// ellipsis ticks on <see cref="DefaultEllipsisStep"/> while visible.</item>
/// </list>
///
/// Timers are plain thread-pool timers: the core has no WPF dependency, events
/// can arrive on any thread (WPF consumers marshal), and disposing stops every
/// timer so a closed Window never leaks callbacks. After disposal, Begin
/// returns a no-op scope and End is a no-op, so a late continuation racing a
/// Window close can never throw or revive the state.
/// </summary>
public sealed class BusyState : IDisposable
{
    /// <summary>Delay before an operation becomes visually busy (no flicker for fast work).</summary>
    public static readonly TimeSpan DefaultDisplayDelay = TimeSpan.FromMilliseconds(180);

    /// <summary>Interval between animated ellipsis steps (one dot per step).</summary>
    public static readonly TimeSpan DefaultEllipsisStep = TimeSpan.FromMilliseconds(400);

    /// <summary>Highest ellipsis step before the dots loop back to one.</summary>
    public const int MaxEllipsisDots = 3;

    private readonly object _gate = new();
    private readonly TimeSpan _displayDelay;
    private readonly TimeSpan _ellipsisStep;
    private readonly List<BusyScope> _scopes = [];
    private Timer? _showTimer;
    private Timer? _dotsTimer;
    private bool _busyVisible;
    private int _dots;
    private bool _disposed;

    public BusyState()
        : this(DefaultDisplayDelay, DefaultEllipsisStep)
    {
    }

    public BusyState(TimeSpan displayDelay, TimeSpan ellipsisStep)
    {
        _displayDelay = displayDelay;
        _ellipsisStep = ellipsisStep;
    }

    /// <summary>Raised when <see cref="IsBusy"/> changes (logical busy).</summary>
    public event EventHandler? BusyChanged;

    /// <summary>Raised when <see cref="IsBusyVisible"/> changes or the operation text changes while visible.</summary>
    public event EventHandler? BusyVisibleChanged;

    /// <summary>Raised whenever the animated ellipsis step changes while visible.</summary>
    public event EventHandler? EllipsisChanged;

    /// <summary>True while at least one operation is active. Thread-safe.</summary>
    public bool IsBusy
    {
        get
        {
            lock (_gate)
            {
                return !_disposed && _scopes.Count > 0;
            }
        }
    }

    /// <summary>True while the dimmed overlay and animated status should be presented.</summary>
    public bool IsBusyVisible
    {
        get
        {
            lock (_gate)
            {
                return _busyVisible;
            }
        }
    }

    /// <summary>
    /// The innermost active operation ("Loading benchmark library") without
    /// any animated dots, or null when idle.
    /// </summary>
    public string? OperationText
    {
        get
        {
            lock (_gate)
            {
                return _scopes.Count > 0 ? _scopes[^1].Operation : null;
            }
        }
    }

    /// <summary>Current ellipsis step (1-3) while visible, 0 when not busy.</summary>
    public int EllipsisDots
    {
        get
        {
            lock (_gate)
            {
                return _dots;
            }
        }
    }

    public bool IsDisposed
    {
        get
        {
            lock (_gate)
            {
                return _disposed;
            }
        }
    }

    /// <summary>
    /// Opens one busy scope. The window stays busy until the returned scope is
    /// disposed (or until <see cref="Dispose"/> ends everything). The operation
    /// message must describe the real work; animated dots are added later by
    /// the presentation layer, never stored here.
    /// </summary>
    public BusyScope Begin(string operation) => BeginCore(operation, showImmediately: false);

    /// <summary>
    /// Opens a scope whose status bar and dim overlay become visible immediately.
    /// Use this for deliberate user actions which are known to rebuild the
    /// workspace, where a visible working state is preferable to the normal
    /// no-flicker delay.
    /// </summary>
    public BusyScope BeginVisible(string operation) => BeginCore(operation, showImmediately: true);

    private BusyScope BeginCore(string operation, bool showImmediately)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);

        bool becameBusy;
        bool visibleTextChanged;
        bool becameVisible = false;
        BusyScope scope;
        lock (_gate)
        {
            if (_disposed)
            {
                return BusyScope.NoOp;
            }

            scope = new BusyScope(this, operation);
            _scopes.Add(scope);
            becameBusy = _scopes.Count == 1;
            if (becameBusy)
            {
                if (showImmediately)
                {
                    StartVisibleTimers();
                    becameVisible = true;
                }
                else
                {
                    _showTimer = new Timer(OnShowTimerTick, null, _displayDelay, Timeout.InfiniteTimeSpan);
                }
            }
            else if (showImmediately && !_busyVisible)
            {
                _showTimer?.Dispose();
                _showTimer = null;
                StartVisibleTimers();
                becameVisible = true;
            }

            visibleTextChanged = !becameBusy && _busyVisible;
        }

        if (becameBusy)
        {
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }

        if (becameVisible || visibleTextChanged)
        {
            // The visible text follows the innermost operation.
            BusyVisibleChanged?.Invoke(this, EventArgs.Empty);
        }

        if (becameVisible)
        {
            EllipsisChanged?.Invoke(this, EventArgs.Empty);
        }

        return scope;
    }

    /// <summary>Runs work inside one busy scope; the scope always ends, even when the work throws.</summary>
    public async Task RunAsync(string operation, Func<Task> work)
    {
        using var scope = Begin(operation);
        await work();
    }

    /// <summary>Runs work inside one busy scope and returns its result; exception-safe.</summary>
    public async Task<T> RunAsync<T>(string operation, Func<Task<T>> work)
    {
        using var scope = Begin(operation);
        return await work();
    }

    /// <summary>
    /// Runs CPU-bound or blocking work on the thread pool inside one busy
    /// scope, so the UI thread keeps rendering (the animated dots included).
    /// </summary>
    public async Task<T> RunOnThreadPoolAsync<T>(string operation, Func<T> compute) =>
        await RunAsync(operation, () => Task.Run(compute));

    /// <summary>
    /// Runs CPU-bound or blocking work on the thread pool inside one busy
    /// scope, so the UI thread keeps rendering (the animated dots included).
    /// </summary>
    public Task RunOnThreadPoolAsync(string operation, Action compute) =>
        RunAsync(operation, () => Task.Run(compute));

    /// <summary>
    /// Ends one scope. Safe from any thread and after disposal (no-op then).
    /// Only when the last scope ends does the state return to READY.
    /// </summary>
    internal void End(BusyScope scope)
    {
        bool becameIdle = false;
        bool visibleTextChanged = false;
        lock (_gate)
        {
            if (_disposed || !_scopes.Remove(scope))
            {
                return;
            }

            if (_scopes.Count > 0)
            {
                visibleTextChanged = _busyVisible;
            }
            else
            {
                becameIdle = true;
                visibleTextChanged = true;
                StopTimers();
                _busyVisible = false;
                _dots = 0;
            }
        }

        if (becameIdle)
        {
            BusyChanged?.Invoke(this, EventArgs.Empty);
        }

        if (visibleTextChanged)
        {
            BusyVisibleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Ends every active scope, stops both timers, and suppresses all future
    /// activity. Called when the owning Window closes; any in-flight operation
    /// completing afterwards hits the no-op path.
    /// </summary>
    public void Dispose()
    {
        bool hadState;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            hadState = _scopes.Count > 0 || _busyVisible;
            _scopes.Clear();
            StopTimers();
            _busyVisible = false;
            _dots = 0;
        }

        if (hadState)
        {
            BusyChanged?.Invoke(this, EventArgs.Empty);
            BusyVisibleChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    private void StartVisibleTimers()
    {
        _busyVisible = true;
        _dots = 1;
        _dotsTimer?.Dispose();
        _dotsTimer = new Timer(OnDotsTimerTick, null, _ellipsisStep, _ellipsisStep);
    }

    private void OnShowTimerTick(object? state)
    {
        lock (_gate)
        {
            if (_disposed || _scopes.Count == 0 || _busyVisible)
            {
                _showTimer?.Dispose();
                _showTimer = null;
                return;
            }

            // One-shot presentation threshold: the operation outlived the
            // delay, so it is worth showing.
            _showTimer?.Dispose();
            _showTimer = null;
            StartVisibleTimers();
        }

        BusyVisibleChanged?.Invoke(this, EventArgs.Empty);
        EllipsisChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnDotsTimerTick(object? state)
    {
        lock (_gate)
        {
            if (_disposed || !_busyVisible)
            {
                return;
            }

            _dots = _dots >= MaxEllipsisDots ? 1 : _dots + 1;
        }

        EllipsisChanged?.Invoke(this, EventArgs.Empty);
    }

    private void StopTimers()
    {
        _showTimer?.Dispose();
        _showTimer = null;
        _dotsTimer?.Dispose();
        _dotsTimer = null;
    }
}
