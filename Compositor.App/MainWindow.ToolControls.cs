using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Compositor.App;

public partial class MainWindow
{
    private void InitializeToolControls()
    {
        LinkSlider(SizeSlider, BrushSize);
        LinkSlider(HardnessSlider, BrushHardness);
        LinkSlider(OpacitySlider, BrushOpacity);
        LinkSlider(MaskSlider, MaskGray);
        LinkSlider(LayerOpacitySlider, LayerOpacity);
        Canvas.SizeChanged += (_, _) => InspectorScroll.MaxHeight = Math.Clamp(Canvas.ActualHeight - 310, 80, 300);
        BrushColor.TextChanged += (_, _) =>
        {
            if (ColorPickerWindow.TryHex(BrushColor.Text, out var color))
            {
                ColorSwatch.Background = ToolbarColorSwatch.Background = new SolidColorBrush(color);
                ToolbarColorButton.ToolTip = $"Choose brush color · {ColorPickerWindow.Hex(color)}";
            }
        };
        RefreshToolControls();
    }

    private void LinkSlider(Slider slider, TextBox input)
    {
        bool updating = false;
        slider.ValueChanged += (_, _) =>
        {
            if (updating) return;
            updating = true; input.Text = Math.Round(slider.Value).ToString(CultureInfo.InvariantCulture); updating = false;
        };
        input.TextChanged += (_, _) =>
        {
            if (updating || !double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ||
                !double.IsFinite(number) || number < slider.Minimum || number > slider.Maximum) return;
            updating = true; slider.Value = number; updating = false;
        };
        void Commit()
        {
            if (double.TryParse(input.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double number) && double.IsFinite(number))
                slider.Value = Math.Clamp(number, slider.Minimum, slider.Maximum);
            input.Text = slider.Value.ToString("0.###", CultureInfo.InvariantCulture);
        }
        input.LostKeyboardFocus += (_, _) => Commit();
        input.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Commit(); Canvas.Focus(); e.Handled = true; } };
    }

    private void RefreshToolControls()
    {
        if (BrushOptions is null) return;
        bool painting = ToolPicker.SelectedIndex is 1 or 2;
        ToolTitle.Text = ToolPicker.SelectedIndex switch { 0 => "Move", 2 => "Eraser", 3 => "Hand", _ => "Brush" };
        ColorButton.IsEnabled = ToolbarColorButton.IsEnabled = !session.EditMask;
        ToolHint.Visibility = painting ? Visibility.Collapsed : Visibility.Visible;
        BrushOptions.Visibility = painting ? Visibility.Visible : Visibility.Collapsed;
        ColorOptions.Visibility = !session.EditMask && ToolPicker.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        MaskOptions.Visibility = session.EditMask && ToolPicker.SelectedIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        ToolHint.Text = ToolPicker.SelectedIndex switch
        {
            0 => "Drag to move layer · Esc to cancel",
            3 => "Drag to pan · Wheel to zoom",
            2 => "Erase pixels · Wheel to zoom",
            _ => session.EditMask ? "Black hides · White reveals" : "Paint · Wheel to zoom · Middle drag to pan"
        };
    }

    private void PickBrushColor(object sender, RoutedEventArgs e)
    {
        if (busy || session.InTransaction || session.EditMask) return;
        if (!ColorPickerWindow.TryHex(BrushColor.Text, out var color)) color = Colors.Black;
        var dialog = new ColorPickerWindow(color) { Owner = this };
        if (dialog.ShowDialog() == true) BrushColor.Text = ColorPickerWindow.Hex(dialog.SelectedColor);
        Canvas.Focus();
    }
    private void MaskBlack(object sender, RoutedEventArgs e) => MaskSlider.Value = 0;
    private void MaskWhite(object sender, RoutedEventArgs e) => MaskSlider.Value = 100;
}
