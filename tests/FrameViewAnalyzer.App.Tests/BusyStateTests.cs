using System.Collections.Concurrent;
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
                "Loading base capture",
                () => throw new InvalidOperationException("Boom")));

        Assert.False(state.IsBusy);
        Assert.False(state.IsBusyVisible);
        Assert.Null(state.OperationText);
    }

    [Fact]
    public async Task RunAsync_returns_the_result_and_restores_ready()
    {
        var state = new BusyState();

        var result = await state.RunAsync("Processing capture data", () => Task.FromResult(42));

        Assert.Equal(42, result);
        Assert.False(state.IsBusy);
    }

    [Fact]
    public void Nested_scopes_stay_busy_until_every_scope_ends()
    {
        var state = new BusyState();

        var outer = state.Begin("Loading base capture");
        var inner = state.Begin("Processing capture data");
        outer.Dispose();

        // The window must not return to READY while the inner work continues.
        Assert.True(state.IsBusy);
        Assert.Equal("Processing capture data", state.OperationText);

        inner.Dispose();
        Assert.False(state.IsBusy);
        Assert.Null(state.OperationText);
    }

    [Fact]
    public void Interleaved_scope_end_restores_the_previous_operation()
    {
        var state = new BusyState();

        var first = state.Begin("Loading base capture");
        var second = state.Begin("Loading comparison capture");
        first.Dispose();

        Assert.Equal("Loading comparison capture", state.OperationText);

        second.Dispose();
        Assert.Null(state.OperationText);
    }

    [Fact]
    public void BeginVisible_shows_the_status_and_overlay_immediately()
    {
        var state = new BusyState();

        using (state.BeginVisible("Reanalyzing benchmark"))
        {
            Assert.True(state.IsBusy);
            Assert.True(state.IsBusyVisible);
            Assert.Equal("Reanalyzing benchmark", state.OperationText);
            Assert.Equal(1, state.EllipsisDots);
        }

        Assert.False(state.IsBusy);
        Assert.False(state.IsBusyVisible);
        Assert.Equal(0, state.EllipsisDots);
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

        // EllipsisChanged fires from BusyState's thread-pool timers, so the
        // test captures values into a thread-safe queue and awaits a signal
        // once the full 1 → 2 → 3 cycle has been observed — no Task.Delay
        // racing the timer and no unsynchronized List access.
        var dots = new ConcurrentQueue<int>();
        var cycleObserved = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var nextExpectedDot = 1;
        state.EllipsisChanged += (_, _) =>
        {
            var dot = state.EllipsisDots;
            if (dot == 0)
            {
                return;
            }

            dots.Enqueue(dot);
            if (dot == nextExpectedDot)
            {
                nextExpectedDot++;
                if (nextExpectedDot > BusyState.MaxEllipsisDots)
                {
                    cycleObserved.TrySetResult();
                }
            }
            else if (dot == 1)
            {
                // The cycle wrapped before the expected step was observed (a
                // handler can run after the next tick already advanced the
                // state): restart the pattern from the fresh 1.
                nextExpectedDot = 2;
            }
        };

        var scope = state.Begin("Loading benchmark library");
        try
        {
            // Generous ceiling: the pattern completes in well under a second;
            // the timeout only guards against a regression that never ticks.
            await cycleObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));

            Assert.True(state.IsBusyVisible);

            // The signal only completes on an observed 1 → 2 → 3 run, but
            // assert the order independently from the captured values too.
            var captured = dots.ToArray();
            Assert.True(
                ContainsOrderedCycle(captured),
                $"Dots must cycle in order 1 → 2 → 3, captured: {string.Join(", ", captured)}");
        }
        finally
        {
            scope.Dispose();
        }

        Assert.False(state.IsBusyVisible);
        Assert.Equal(0, state.EllipsisDots);
    }

    /// <summary>True when the values contain 1, then 2, then 3 in that order.</summary>
    private static bool ContainsOrderedCycle(IEnumerable<int> values)
    {
        var expected = 1;
        foreach (var value in values)
        {
            if (value == expected)
            {
                expected++;
                if (expected > BusyState.MaxEllipsisDots)
                {
                    return true;
                }
            }
            else if (value == 1)
            {
                // The pattern restarts at the fresh 1 after a wrap.
                expected = 2;
            }
        }

        return false;
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
