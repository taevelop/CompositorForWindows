using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Compositor.Core;

namespace Compositor.App;

public partial class MainWindow
{
    private async Task ToolControlsSmokeTest(string reportPath)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var document = session.Document; int undo = session.UndoCount, redo = session.RedoCount;
        for (int tool = 0; tool < 4; tool++)
        {
            ToolPicker.SelectedIndex = tool;
            Check((int)Canvas.Tool == tool, "Tool button and canvas disagree.");
            Check(BrushOptions.Visibility == (tool is 1 or 2 ? Visibility.Visible : Visibility.Collapsed), "Irrelevant brush settings are visible.");
        }
        ToolPicker.SelectedIndex = 1;
        SizeSlider.Value = 125; HardnessSlider.Value = 35; OpacitySlider.Value = 45;
        Check(ReadBrush().Diameter == 125 && ReadBrush().Hardness == .35 && ReadBrush().Opacity == .45, "Sliders did not update brush settings.");
        BrushSize.Text = "800"; BrushHardness.Text = "80";
        Check(SizeSlider.Value == 800 && HardnessSlider.Value == 80, "Typed values did not update sliders.");
        BrushSize.Text = "NaN"; BrushSize.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, BrushSize, Canvas) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        Check(BrushSize.Text == "800", "Invalid draft did not recover the last valid value.");
        BrushSize.Text = "3000"; BrushSize.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice, Environment.TickCount, BrushSize, Canvas) { RoutedEvent = Keyboard.LostKeyboardFocusEvent });
        Check(ReadBrush().Diameter == 2000, "Size did not clamp to the supported range.");
        Check(ReferenceEquals(document, session.Document) && session.UndoCount == undo && session.RedoCount == redo, "Tool options edited the document/history.");

        bool? ShowPicker(Action<ColorPickerWindow> action, out Color selected)
        {
            var dialog = new ColorPickerWindow(Color.FromRgb(98, 201, 181)) { Owner = this }; Exception? failure = null;
            dialog.Loaded += (_, _) => { try { action(dialog); } catch (Exception ex) { failure = ex; } finally { if (dialog.IsVisible) dialog.Close(); } };
            bool? result = dialog.ShowDialog(); selected = dialog.SelectedColor;
            if (failure is not null) throw new InvalidOperationException("Color picker failed.", failure);
            return result;
        }
        string previous = BrushColor.Text;
        Check(ShowPicker(d => { d.SetColor(Colors.Red); d.Close(); }, out _) != true && BrushColor.Text == previous, "Cancel changed the brush color.");
        Check(ShowPicker(d =>
        {
            d.HexInput.Text = "#NOTHEX"; Check(!d.ApplyButton.IsEnabled, "Invalid hex is accepted.");
            d.HexInput.Text = "#FF0000"; d.Hue.Value = 120;
            Check(d.SelectedColor == Colors.Lime, "Hue strip did not update the color.");
            d.Field.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(d), Environment.TickCount, Key.Left) { RoutedEvent = Keyboard.KeyDownEvent });
            Check(d.SelectedColor.R > 0 && d.SelectedColor.G == 255, "Color field keyboard adjustment failed.");
            d.SetColor(Color.FromRgb(98, 201, 181)); d.UpdateLayout();
            var image = new RenderTargetBitmap((int)d.ActualWidth, (int)d.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(d);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(Path.ChangeExtension(reportPath, ".picker.png"))) encoder.Save(output);
            d.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, out var chosen) == true, "Color could not be applied.");
        BrushColor.Text = ColorPickerWindow.Hex(chosen);
        Check(ReadBrush().Red == 98 && ReadBrush().Green == 201 && ReadBrush().Blue == 181, "Chosen color not used by brush.");
        foreach (var color in new[] { Colors.Black, Colors.White, Colors.Red, Colors.Lime, Colors.Blue, Color.FromRgb(23, 155, 89) })
        { var field = new ColorField(); field.SetColor(color); Check(field.SelectedColor == color, "RGB/HSV round trip changed color."); }

        if (session.ActiveLayer?.Mask is null) AddRevealMask(this, new());
        EditTarget.SelectedIndex = 1; Refresh(); ToolPicker.SelectedIndex = 1;
        Check(MaskOptions.Visibility == Visibility.Visible && ColorOptions.Visibility == Visibility.Collapsed, "Mask controls not contextual.");
        MaskBlack(this, new()); Check(ReadBrush().Red == 0, "Black mask preset failed.");
        MaskWhite(this, new()); Check(ReadBrush().Red == 255, "White mask preset failed.");
        EditTarget.SelectedIndex = 0; Refresh();
        Check(ColorOptions.Visibility == Visibility.Visible && MaskOptions.Visibility == Visibility.Collapsed, "Image color controls not restored.");
        SizeSlider.Value = 180; HardnessSlider.Value = 60; OpacitySlider.Value = 100;
        double width = Width; Width = 1000; UpdateLayout();
        Check(BrushOptions.ActualWidth <= Editor.ActualWidth, "Toolbar overflows minimum window width.");
        Width = width; UpdateLayout(); Canvas.Fit();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow,
            checks = new[] { "four direct tool selectors", "contextual options", "slider/number synchronization", "invalid draft and bounds", "settings preserve document/history", "picker cancel and apply", "hex validation", "hue and keyboard color field", "RGB/HSV round trip", "mask presets", "minimum width layout" },
            note = "Hidden WPF control checks; a changed toolbar still needs a user usability check." }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
