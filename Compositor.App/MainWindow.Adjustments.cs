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
        RefreshEffectsControls();
        AdjustColorsMenu.IsEnabled = AdjustColorsButton.IsEnabled = session.ActiveLayer is { IsGroup: false, IsAdjustment: false } && !session.EditMask;
        bool exposure = session.ActiveLayer?.Exposure is not null;
        EditExposureMenu.IsEnabled = exposure;
        EditExposureButton.Visibility = exposure ? Visibility.Visible : Visibility.Collapsed;
        bool levels = session.ActiveLayer?.Levels is not null;
        EditLevelsMenu.IsEnabled = levels;
        EditLevelsButton.Visibility = levels ? Visibility.Visible : Visibility.Collapsed;
        bool curves = session.ActiveLayer?.Curves is not null;
        EditCurvesMenu.IsEnabled = curves;
        EditCurvesButton.Visibility = curves ? Visibility.Visible : Visibility.Collapsed;
        ExposureHint.Visibility = exposure || levels || curves ? Visibility.Visible : Visibility.Collapsed;
    }
}
