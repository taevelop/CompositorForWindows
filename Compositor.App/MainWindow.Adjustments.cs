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
        EditColorBalanceMenu.IsEnabled=session.ActiveLayer?.ColorBalance is not null; EditColorBalanceButton.Visibility=EditColorBalanceMenu.IsEnabled?Visibility.Visible:Visibility.Collapsed;
        EditHueSaturationMenu.IsEnabled=session.ActiveLayer?.HueSaturation is not null; EditHueSaturationButton.Visibility=EditHueSaturationMenu.IsEnabled?Visibility.Visible:Visibility.Collapsed;
        EditGradientMapMenu.IsEnabled=session.ActiveLayer?.GradientMap is not null; EditGradientMapButton.Visibility=EditGradientMapMenu.IsEnabled?Visibility.Visible:Visibility.Collapsed;
        EditGrainMenu.IsEnabled=session.ActiveLayer?.Grain is not null; EditGrainButton.Visibility=EditGrainMenu.IsEnabled?Visibility.Visible:Visibility.Collapsed;
        AdjustColorsMenu.IsEnabled = AdjustColorsButton.IsEnabled = session.ActiveLayer is { IsGroup: false, IsAdjustment: false } && !session.EditMask;
        bool blackWhite = session.ActiveLayer?.BlackWhite is not null; EditBlackWhiteMenu.IsEnabled = blackWhite; EditBlackWhiteButton.Visibility = blackWhite ? Visibility.Visible : Visibility.Collapsed;
        bool exposure = session.ActiveLayer?.Exposure is not null;
        EditExposureMenu.IsEnabled = exposure;
        EditExposureButton.Visibility = exposure ? Visibility.Visible : Visibility.Collapsed;
        bool levels = session.ActiveLayer?.Levels is not null;
        EditLevelsMenu.IsEnabled = levels;
        EditLevelsButton.Visibility = levels ? Visibility.Visible : Visibility.Collapsed;
        bool curves = session.ActiveLayer?.Curves is not null;
        EditCurvesMenu.IsEnabled = curves;
        EditCurvesButton.Visibility = curves ? Visibility.Visible : Visibility.Collapsed;
        ExposureHint.Visibility = session.ActiveLayer?.IsAdjustment == true ? Visibility.Visible : Visibility.Collapsed;
    }
}
