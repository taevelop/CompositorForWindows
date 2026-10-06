using System.Windows;

namespace Compositor.App;

public partial class MainWindow
{
    private void EditColorOverlay(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var dialog = new ColorOverlayWindow(session) { Owner = this };
        try { dialog.ShowDialog(); }
        finally { dialog.CancelEdit(); Refresh(); Canvas.Focus(); }
    });
    private void EditShadow(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var dialog = new ShadowWindow(session) { Owner = this };
        try { dialog.ShowDialog(); }
        finally { dialog.CancelEdit(); Refresh(); Canvas.Focus(); }
    });
    private void EditStroke(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var dialog = new StrokeWindow(session) { Owner = this };
        try { dialog.ShowDialog(); }
        finally { dialog.CancelEdit(); Refresh(); Canvas.Focus(); }
    });
    private void EditInnerShadow(object sender, RoutedEventArgs e) => Safe(() => { var dialog = new InnerShadowWindow(session) { Owner = this }; try { dialog.ShowDialog(); } finally { dialog.CancelEdit(); Refresh(); Canvas.Focus(); } });
    private void EditOuterGlow(object sender, RoutedEventArgs e) => Safe(() => { var dialog = new OuterGlowWindow(session) { Owner = this }; try { dialog.ShowDialog(); } finally { dialog.CancelEdit(); Refresh(); Canvas.Focus(); } });
    private void RefreshEffectsControls()
    {
        InnerShadowMenu.IsEnabled = InnerShadowButton.IsEnabled = session.ActiveLayer is { IsGroup: false, IsAdjustment: false };
        OuterGlowMenu.IsEnabled = OuterGlowButton.IsEnabled = session.ActiveLayer is { IsGroup: false, IsAdjustment: false };
        StrokeMenu.IsEnabled = StrokeButton.IsEnabled = session.ActiveLayer is { IsGroup: false, IsAdjustment: false };
        ShadowMenu.IsEnabled = ShadowButton.IsEnabled = session.ActiveLayer is { IsGroup: false, IsAdjustment: false };
        ColorOverlayMenu.IsEnabled = ColorOverlayButton.IsEnabled = session.ActiveLayer is { IsGroup: false, IsAdjustment: false };
        var overlay = session.ActiveLayer?.Effects?.ColorOverlay;
        ColorOverlayInfo.Text = overlay is null ? "No color overlay" : overlay.IsEnabled ? "Color overlay enabled" : "Color overlay disabled";
    }
}
