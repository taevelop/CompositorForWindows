using System.Windows;

namespace Compositor.App;

public partial class MainWindow
{
    private void AdjustColors(object sender, RoutedEventArgs e) => Safe(() =>
    {
        var dialog = new ColorAdjustmentWindow(session) { Owner = this };
        try { dialog.ShowDialog(); }
        finally { dialog.CancelEdit(); Refresh(); Canvas.Focus(); }
    });
    private void RefreshAdjustmentControls()
    {
        AdjustColorsMenu.IsEnabled = AdjustColorsButton.IsEnabled = session.ActiveLayer is { IsGroup: false } && !session.EditMask;
    }
}
