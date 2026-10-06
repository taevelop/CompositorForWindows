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
        bool transforming = Canvas?.IsTransforming == true;
        bool painting = ToolPicker.SelectedIndex is 1 or 2;
        ToolTitle.Text = ToolPicker.SelectedIndex switch { 0 => "Move", 2 => "Eraser", 3 => "Hand", 4 => "Rectangle", 5 => "Ellipse", 6 => "Lasso", 7 => "Polygon", 8 => "Crop", _ => "Brush" };
        if (transforming) ToolTitle.Text = "Transform";
        ColorButton.IsEnabled = ToolbarColorButton.IsEnabled = !session.EditMask;
        bool selecting = ToolPicker.SelectedIndex is 4 or 5 or 6 or 7;
        SelectionOptions.Visibility = selecting ? Visibility.Visible : Visibility.Collapsed;
        SelectionCenter.Visibility = ToolPicker.SelectedIndex is 4 or 5 ? Visibility.Visible : Visibility.Collapsed;
        SelectionHint.Text = ToolPicker.SelectedIndex == 7 ? "Enter: close · Backspace: point · Ctrl-drag: pixels" : ToolPicker.SelectedIndex == 6 ? "Draw · Ctrl-drag: pixels · Esc: cancel" : "Shift: square · Ctrl-drag: pixels · Esc: cancel";
        CropOptions.Visibility = ToolPicker.SelectedIndex == 8 ? Visibility.Visible : Visibility.Collapsed;
        ToolHint.Visibility = transforming || painting || selecting || ToolPicker.SelectedIndex == 8 ? Visibility.Collapsed : Visibility.Visible;
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
