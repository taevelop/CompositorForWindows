using System.Windows;
using System.Windows.Controls;
using Compositor.Core;

namespace Compositor.App;

public partial class MainWindow
{
    private void RefreshMaskControls()
    {
        var mask = session.ActiveLayer?.Mask;
        MaskSection.IsEnabled = session.ActiveLayer is not null;
        if (mask is not null) MaskSection.IsExpanded = true;
        MaskInfo.Text = mask is null ? "No mask" : $"{mask.Pixels.Width:N0} × {mask.Pixels.Height:N0} · {(mask.Enabled ? "Enabled" : "Disabled")}";
        RevealMaskButton.IsEnabled = HideMaskButton.IsEnabled = session.ActiveLayer is not null && mask is null;
        RemoveMaskButton.IsEnabled = MaskEnabled.IsEnabled = EditTarget.IsEnabled = mask is not null;
        MaskEnabled.IsChecked = mask?.Enabled == true;
        EditTarget.SelectedIndex = session.EditMask ? 1 : 0;
        MaskGray.IsEnabled = session.EditMask; BrushColor.IsEnabled = !session.EditMask;
    }
    private void AddRevealMask(object sender, RoutedEventArgs e) => AddMask(255);
    private void AddHideMask(object sender, RoutedEventArgs e) => AddMask(0);
    private void AddMask(byte value) => Safe(() =>
    {
        if (session.ActiveLayer is not { Mask: null } layer) return;
        var mask = LayerMask.Solid(1, 1, value);
        session.Apply(d => d.Replace(layer with { Mask = mask }));
        session.EditMask = true; Refresh();
    });
    private void RemoveMask(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (session.ActiveLayer is not { Mask: not null } layer) return;
        session.Apply(d => d.Replace(layer with { Mask = null }));
    });
    private void ToggleMask(object sender, RoutedEventArgs e) => Safe(() =>
    {
        if (session.ActiveLayer is not { Mask: { } mask } layer) return;
        session.Apply(d => d.Replace(layer with { Mask = mask with { Enabled = MaskEnabled.IsChecked == true } }));
    });
    private void EditTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || busy || Canvas?.Session is null) return;
        Canvas.CancelInteraction();
        session.EditMask = EditTarget.SelectedIndex == 1 && session.ActiveLayer?.Mask is not null;
        Refresh(); Canvas.Focus();
    }
}
