using System.Windows;
using System.Windows.Controls;
using FrameViewAnalyzer.App.Views;

namespace FrameViewAnalyzer.App.Tests;

public class ThemedDialogWindowTests
{
    [Fact]
    public void Dialog_variants_use_the_expected_semantic_icon_treatment() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();

            var cases = new[]
            {
                (ThemedDialogKind.Info, "i", "SeriesBBrush"),
                (ThemedDialogKind.Warning, "!", "WarningBrush"),
                (ThemedDialogKind.Error, "×", "DangerBrush"),
                (ThemedDialogKind.Success, "✓", "SuccessBrush"),
                (ThemedDialogKind.Confirmation, "?", "WarningBrush"),
            };

            foreach (var (kind, glyph, brush) in cases)
            {
                var dialog = new ThemedDialogWindow(kind, "Title", "Message");
                Assert.Equal(kind, dialog.Kind);
                Assert.Equal(glyph, dialog.IconGlyphText);
                Assert.Equal(brush, dialog.IconBrushResource);
                Assert.Equal("Title", dialog.Title);
                Assert.Equal(ResizeMode.NoResize, dialog.ResizeMode);
                Assert.False(dialog.ShowInTaskbar);
            }
        });

    [Fact]
    public void Dialog_uses_the_compact_layout_contract() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var dialog = new ThemedDialogWindow(ThemedDialogKind.Info, "Export", "Saved successfully.");

            Assert.Equal(440, dialog.Width);
            Assert.Equal(360, dialog.MinWidth);
            Assert.Equal(560, dialog.MaxWidth);
            Assert.Equal(480, dialog.MaxHeight);

            var primary = Assert.IsType<Button>(dialog.FindName("PrimaryButton"));
            Assert.Equal(78, primary.MinWidth);
        });

    [Fact]
    public void Confirmation_has_a_safe_default_and_explicit_cancel_action() =>
        WpfStaTestHost.Run(() =>
        {
            WpfStaTestHost.EnsureApplication();
            var dialog = new ThemedDialogWindow(
                ThemedDialogKind.Confirmation,
                "Remove from Library",
                "Remove this benchmark?",
                primaryText: "Remove",
                cancelText: "Cancel",
                destructive: true);

            Assert.True(dialog.HasCancelAction);
            var primary = Assert.IsType<Button>(dialog.FindName("PrimaryButton"));
            var cancel = Assert.IsType<Button>(dialog.FindName("CancelButton"));
            Assert.Equal("Remove", primary.Content);
            Assert.Equal("Cancel", cancel.Content);
            Assert.False(primary.IsDefault);
            Assert.True(cancel.IsDefault);
            Assert.True(cancel.IsCancel);
        });
}
