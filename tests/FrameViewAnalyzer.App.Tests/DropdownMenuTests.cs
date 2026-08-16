using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using FrameViewAnalyzer.App.Views;

namespace FrameViewAnalyzer.App.Tests;

/// <summary>
/// Regression coverage for the left-click dropdown helper: the Export, Analyze
/// and Capture-folder buttons must map to their own ContextMenu, anchored
/// below the button, without duplicating any menu logic.
/// </summary>
public class DropdownMenuTests
{
    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            throw failure;
        }
    }

    [Fact]
    public void Prepare_open_resolves_and_anchors_the_buttons_context_menu() =>
        RunSta(() =>
        {
            var button = new Button { ContextMenu = new ContextMenu() };

            var prepared = DropdownMenu.TryPrepareOpen(button, out var menu);

            Assert.True(prepared);
            Assert.NotNull(menu);
            Assert.Same(button, menu.PlacementTarget);
            Assert.Equal(PlacementMode.Bottom, menu.Placement);
        });

    [Fact]
    public void Prepare_open_returns_false_when_the_button_has_no_context_menu() =>
        RunSta(() =>
        {
            var button = new Button();

            var prepared = DropdownMenu.TryPrepareOpen(button, out var menu);

            Assert.False(prepared);
            Assert.Null(menu);
        });

    [Fact]
    public void Attached_property_round_trips_on_a_button() =>
        RunSta(() =>
        {
            var button = new Button();

            Assert.False(DropdownMenu.GetOpenOnLeftClick(button));
            DropdownMenu.SetOpenOnLeftClick(button, true);
            Assert.True(DropdownMenu.GetOpenOnLeftClick(button));
            DropdownMenu.SetOpenOnLeftClick(button, false);
            Assert.False(DropdownMenu.GetOpenOnLeftClick(button));
        });

    [Fact]
    public void Each_dropdown_button_keeps_its_own_context_menu() =>
        RunSta(() =>
        {
            var export = new Button { ContextMenu = new ContextMenu() };
            var analyze = new Button { ContextMenu = new ContextMenu() };

            DropdownMenu.TryPrepareOpen(export, out var exportMenu);
            DropdownMenu.TryPrepareOpen(analyze, out var analyzeMenu);

            Assert.NotNull(exportMenu);
            Assert.NotNull(analyzeMenu);
            Assert.NotSame(exportMenu, analyzeMenu);
            Assert.Same(export, exportMenu.PlacementTarget);
            Assert.Same(analyze, analyzeMenu.PlacementTarget);
        });
}
