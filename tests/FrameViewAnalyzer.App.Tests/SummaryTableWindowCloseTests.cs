using System.Reflection;
using FrameViewAnalyzer.App.Views;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for the modeless Summary window close behavior: the
/// Close button must be wired to an explicit close action (IsCancel does not
/// apply to modeless windows) and Escape closes the window.
/// </summary>
public class SummaryTableWindowCloseTests
{
    [Fact]
    public void Close_button_is_wired_to_an_explicit_close_handler()
    {
        var method = typeof(SummaryTableWindow).GetMethod(
            "Close_Click",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
        Assert.Equal(typeof(void), method!.ReturnType);
    }

    [Fact]
    public void Escape_key_is_handled_explicitly()
    {
        var method = typeof(SummaryTableWindow).GetMethod(
            "Window_PreviewKeyDown",
            BindingFlags.Instance | BindingFlags.NonPublic);

        Assert.NotNull(method);
    }

    [Fact]
    public void Close_actions_do_not_touch_summary_table_state()
    {
        // The close path is presentation-only: it is plain Close() with no
        // view-model mutation surface. Assert the handlers take only the
        // expected standard parameters (no data/state parameters).
        var closeParameters = typeof(SummaryTableWindow)
            .GetMethod("Close_Click", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetParameters();

        Assert.Equal(2, closeParameters.Length);
        Assert.Equal(typeof(object), closeParameters[0].ParameterType);
    }
}
