using FrameViewAnalyzer.App.Busy;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Core busy-state behavior without WPF: ready start, begin/end, exception
/// safety, nesting, the no-flicker presentation threshold, ellipsis cycling,
/// and clean disposal. Timing assertions use generous margins so the tests
/// never depend on exact timer granularity.
/// </summary>
public class BusyStateTests
{
    [Fact]
    public void Window_starts_ready()
    {
        var state = new BusyState();

        Assert.False(state.IsBusy);
        Assert.False(state.IsBusyVisible);
        Assert.Null(state.OperationText);
        Assert.Equal(0, state.EllipsisDots);
        Assert.False(state.IsDisposed);
    }

    [Fact]
    public void Begin_and_dispose_return_to_ready()
    {
        var state = new BusyState();

        var scope = state.Begin("Loading benchmark library");
        Assert.True(state.IsBusy);
        Assert.Equal("Loading benchmark library", state.OperationText);

        scope.Dispose();
        Assert.False(state.IsBusy);
        Assert.Null(state.OperationText);
        Assert.False(state.IsBusyVisible);
    }

    [Fact]
    public void Operation_text_never_contains_hard_coded_dots()
    {
        var state = new BusyState();

        using (state.Begin("Loading benchmark library"))
        {
            Assert.Equal("Loading benchmark library", state.OperationText);
            Assert.DoesNotContain(".", state.OperationText);
        }
    }

    [Fact]
    public async Task RunAsync_restores_ready_when_the_work_throws()
    {
        var state = new BusyState();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            state.RunAsync(
                "Loading base capture...",
                () => throw new InvalidOperationException("Boom")));

        Assert.False(state.IsBusy);
        Assert.False(state.IsBusyVisible);
        Assert.Null(state.OperationText);
    }

    [Fact]
    public async Task RunAsync_returns_the_result_and_restores_ready()
    {
        var state = new BusyState();

        var result = await state.RunAsync("Processing capture data...", () => Task.FromResult(42));

        Assert.Equal(42, result);
        Assert.False(state.IsBusy);
    }

    [Fact]
    public void Nested_scopes_stay_busy_until_every_scope_ends()
    {
        var state = new BusyState();

        var outer = state.Begin("Loading base capture...");
        var inner = state.Begin("Processing capture data...");
        outer.Dispose();

        // The window must not return to READY while the inner work continues.
        Assert.True(state.IsBusy);
        Assert.Equal("Processing capture data...", state.OperationText);

        inner.Dispose();
        Assert.False(state.IsBusy);
        Assert.Null(state.OperationText);
    }

    [Fact]
    public void Interleaved_scope_end_restores_the_previous_operation()
    {
        var state = new BusyState();

        var first = state.Begin("Loading base capture...");
        var second = state.Begin("Loading comparison capture...");
        first.Dispose();

        Assert.Equal("Loading comparison capture...", state.OperationText);

        second.Dispose();
        Assert.Null(state.OperationText);
    }

    [Fact]
    public async Task Fast_operations_never_become_visibly_busy()
    {
        // Presentation threshold far above the operation duration.
        var state = new BusyState(
            TimeSpan.FromMilliseconds(300),
            TimeSpan.FromMilliseconds(100));
        var visibleTransitions = new List<bool>();
        state.BusyVisibleChanged += (_, _) => visibleTransitions.Add(state.IsBusyVisible);

        using (state.Begin("Loading benchmark library"))
        {
            Assert.True(state.IsBusy);
        }

        // Wait well past the threshold: the operation already ended, so the
        // dimmed overlay must never have appeared.
        await Task.Delay(800);

        Assert.False(state.IsBusy);
        Assert.False(state.IsBusyVisible);
        Assert.DoesNotContain(true, visibleTransitions);
    }

    [Fact]
    public async Task Slow_operations_become_visible_and_cycle_the_ellipsis()
    {
        var state = new BusyState(
            TimeSpan.FromMilliseconds(60),
            TimeSpan.FromMilliseconds(120));
        var dots = new List<int>();
        state.EllipsisChanged += (_, _) => dots.Add(state.EllipsisDots);

        var scope = state.Begin("Loading benchmark library");
        try
        {
            await Task.Delay(900);

            Assert.True(state.IsBusyVisible);
            // Dots start at one and cycle 1 → 2 → 3 → 1...
            Assert.Equal(1, dots[0]);
            Assert.Contains(1, dots);
            Assert.Contains(2, dots);
            Assert.Contains(3, dots);
            var first = dots.FindIndex(value => value == 1);
            var second = dots.FindIndex(first + 1, value => value == 2);
            var third = dots.FindIndex(second + 1, value => value == 3);
            Assert.True(first < second && second < third, "Dots must cycle in order 1 → 2 → 3.");
        }
        finally
        {
            scope.Dispose();
        }

        Assert.False(state.IsBusyVisible);
        Assert.Equal(0, state.EllipsisDots);
    }

    [Fact]
    public async Task Dispose_stops_every_timer_and_clears_busy()
    {
        var state = new BusyState(
            TimeSpan.FromMilliseconds(50),
            TimeSpan.FromMilliseconds(100));
        state.Begin("Loading benchmark library");
        await Task.Delay(400);
        Assert.True(state.IsBusyVisible);

        var eventsAfterDispose = 0;
        state.EllipsisChanged += (_, _) => eventsAfterDispose++;
        state.Dispose();

        await Task.Delay(400);

        Assert.Equal(0, eventsAfterDispose);
        Assert.False(state.IsBusyVisible);
        Assert.False(state.IsBusy);
        Assert.Null(state.OperationText);
        Assert.True(state.IsDisposed);
    }

    [Fact]
    public async Task Begin_after_dispose_is_a_safe_no_op()
    {
        var state = new BusyState();
        state.Dispose();

        var scope = state.Begin("Loading benchmark library");
        Assert.False(state.IsBusy);
        Assert.Null(state.OperationText);

        // A late continuation disposing its scope must never throw.
        scope.Dispose();
        await scope.DisposeAsync();

        Assert.False(state.IsBusy);
    }

    [Fact]
    public void Empty_operation_message_is_rejected()
    {
        var state = new BusyState();

        Assert.Throws<ArgumentException>(() => state.Begin(""));
        Assert.Throws<ArgumentException>(() => state.Begin("   "));
        Assert.False(state.IsBusy);
    }
}
