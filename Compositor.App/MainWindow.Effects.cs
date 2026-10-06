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
    private void RefreshEffectsControls()
    {
        ShadowMenu.IsEnabled = ShadowButton.IsEnabled = session.ActiveLayer is { IsGroup: false, IsAdjustment: false };
        ColorOverlayMenu.IsEnabled = ColorOverlayButton.IsEnabled = session.ActiveLayer is { IsGroup: false, IsAdjustment: false };
        var overlay = session.ActiveLayer?.Effects?.ColorOverlay;
        ColorOverlayInfo.Text = overlay is null ? "No color overlay" : overlay.IsEnabled ? "Color overlay enabled" : "Color overlay disabled";
    }
}
