using System.Globalization;
using System.Windows;
using System.Windows.Input;

namespace Compositor.App;
public partial class MainWindow
{
    private bool syncingZoom;
    private void RefreshZoomControls()
    {
        if (ZoomSlider is null || Canvas is null) return;
        syncingZoom = true;ZoomSlider.Value = Canvas.Zoom * 100;
        if (!ZoomPercent.IsKeyboardFocusWithin) ZoomPercent.Text = (Canvas.Zoom * 100).ToString("0.###", CultureInfo.InvariantCulture);
        syncingZoom = false;
    }
    private void ZoomValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || syncingZoom || Canvas.HasInteraction) return;
        Canvas.SetZoomPercent(e.NewValue);RefreshZoomControls();
    }
    private void CommitZoomInput()
    {
        if (double.TryParse(ZoomPercent.Text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) && double.IsFinite(value))
            Canvas.SetZoomPercent(Math.Clamp(value, .1, 3200));
        ZoomPercent.Text = (Canvas.Zoom * 100).ToString("0.###", CultureInfo.InvariantCulture);RefreshZoomControls();
    }
    private void ZoomPercentLostFocus(object sender, KeyboardFocusChangedEventArgs e) => CommitZoomInput();
    private void ZoomPercentKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Enter or Key.Escape) { CommitZoomInput();Canvas.Focus();e.Handled = true; }
        else if (e.Key is Key.Up or Key.Down) { StepZoom(e.Key == Key.Up, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift));e.Handled = true; }
    }
    private void StepZoom(bool up, bool coarse)
    {
        if (!double.TryParse(ZoomPercent.Text.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out double value) || !double.IsFinite(value)) value = Canvas.Zoom * 100;
        value = Math.Clamp(value + (up ? 1 : -1) * (coarse ? 10 : 1), .1, 3200);
        ZoomPercent.Text = value.ToString("0.###", CultureInfo.InvariantCulture);CommitZoomInput();
    }
    private void CycleSmearMode()
    {
        if (busy || Canvas.HasInteraction || session.InTransaction) return;
        switch (Canvas.ReadSmear())
        { case Compositor.Imaging.SmearMode.Liquify: SmearBlur.IsChecked = true;break;case Compositor.Imaging.SmearMode.Blur: SmearSmudge.IsChecked = true;break;default: SmearLiquify.IsChecked = true;break; }
    }
}
