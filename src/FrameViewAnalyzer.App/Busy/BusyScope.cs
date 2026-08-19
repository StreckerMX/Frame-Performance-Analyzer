namespace FrameViewAnalyzer.App.Busy;

/// <summary>
/// One active busy operation, opened by <see cref="BusyState.Begin"/>.
/// Dispose ends the operation exactly once — with <c>using</c>,
/// <c>await using</c>, or a <c>try/finally</c> — so an exception can never
/// leave the Window permanently busy. Disposing more than once, or after the
/// owning state was disposed, is a no-op.
/// </summary>
public sealed class BusyScope : IDisposable, IAsyncDisposable
{
    /// <summary>
    /// Returned by <see cref="BusyState.Begin"/> after the state was disposed
    /// (e.g. a late continuation racing a Window close). It never touches any
    /// state and Dispose is a no-op.
    /// </summary>
    internal static readonly BusyScope NoOp = new(null, string.Empty);

    private BusyState? _owner;

    internal BusyScope(BusyState? owner, string operation)
    {
        _owner = owner;
        Operation = operation;
    }

    /// <summary>The operation message this scope was opened with (no animated dots).</summary>
    public string Operation { get; }

    public void Dispose()
    {
        var owner = Interlocked.Exchange(ref _owner, null);
        owner?.End(this);
    }

    public ValueTask DisposeAsync()
    {
        Dispose();
        return ValueTask.CompletedTask;
    }
}
