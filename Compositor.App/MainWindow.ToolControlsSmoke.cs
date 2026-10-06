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

        void Capture(FrameworkElement element, string suffix)
        {
            element.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth), (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(element);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var output = File.Create(Path.ChangeExtension(reportPath, suffix)); png.Save(output);
        }
        UpdateLayout();
        var titleOrigin = ToolTitle.TranslatePoint(new Point(), this);
        double canvasTop = Canvas.TranslatePoint(new Point(), this).Y;
        foreach (int tool in new[] { 0, 1, 2, 3, 4, 5, 6 })
        {
            ToolPicker.SelectedIndex = tool; UpdateLayout();
            Check((ToolTitle.TranslatePoint(new Point(), this) - titleOrigin).Length < .1, "Tool title moves when switching tools.");
            Check(Math.Abs(Canvas.TranslatePoint(new Point(), this).Y - canvasTop) < .1, "Canvas jumps when switching tools.");
            Capture(this, $".tool-{tool}.png");
        }
        ToolPicker.SelectedIndex = 1;
        foreach (var entry in new[] { ToolbarColorButton, ColorButton })
        {
            Exception? pickerFailure = null;
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, new Action(() =>
            {
                var picker = Application.Current.Windows.OfType<ColorPickerWindow>().SingleOrDefault();
                try
                {
                    Check(picker is not null, "Color button did not open the palette.");
                    picker!.SetColor(Color.FromRgb(92, 132, 196));
                    picker.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                }
                catch (Exception ex) { pickerFailure = ex; }
                finally { if (picker?.IsVisible == true) picker.Close(); }
            }));
            entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            if (pickerFailure is not null) throw pickerFailure;
            Check(BrushColor.Text == "#5C84C4" && ((SolidColorBrush)ToolbarColorSwatch.Background).Color == ((SolidColorBrush)ColorSwatch.Background).Color, "Palette result and swatches disagree.");
        }
        foreach (MenuItem menu in MainMenu.Items)
        {
            menu.IsSubmenuOpen = true;
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(menu.Template.FindName("PART_Popup", menu) is System.Windows.Controls.Primitives.Popup { IsOpen: true }, "Menu popup did not open.");
            var popup = (System.Windows.Controls.Primitives.Popup)menu.Template.FindName("PART_Popup", menu);
            Capture((FrameworkElement)popup.Child, $".menu-{MainMenu.Items.IndexOf(menu)}.png");
            foreach (var item in menu.Items.OfType<MenuItem>())
            {
                item.ApplyTemplate();
                Check(item.Template.FindName("MenuFrame", item) is Border, "Native menu item template leaked into dark menu.");
                if (!item.IsEnabled) Check(((SolidColorBrush)item.Foreground).Color == Color.FromRgb(119, 126, 137), "Disabled menu text is not muted.");
                if (item.HasItems)
                {
                    item.IsSubmenuOpen = true;
                    await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                    var childPopup = (System.Windows.Controls.Primitives.Popup)item.Template.FindName("PART_Popup", item);
                    Check(childPopup.IsOpen, "Adjustment submenu did not open.");
                    Capture((FrameworkElement)childPopup.Child, $".menu-{MainMenu.Items.IndexOf(menu)}-child-{menu.Items.IndexOf(item)}.png");
                    foreach (var child in item.Items.OfType<MenuItem>())
                    {
                        child.ApplyTemplate();
                        Check(child.Template.FindName("MenuFrame", child) is Border, "Submenu lost dark template.");
                        if (!child.IsEnabled) Check(((SolidColorBrush)child.Foreground).Color == Color.FromRgb(119, 126, 137), "Disabled adjustment is not muted.");
                    }
                    item.IsSubmenuOpen = false;
                }
            }
            var enabledItem = menu.Items.OfType<MenuItem>().FirstOrDefault(item => item.IsEnabled);
            if (enabledItem is not null)
            {
                enabledItem.Focus();
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                Check(enabledItem.IsHighlighted, "Keyboard menu selection failed.");
                var frame = (Border)enabledItem.Template.FindName("MenuFrame", enabledItem);
                Check(((SolidColorBrush)frame.Background).Color == ((SolidColorBrush)FindResource("AccentMuted")).Color, "Highlighted menu uses native colors.");
                Capture((FrameworkElement)popup.Child, $".menu-{MainMenu.Items.IndexOf(menu)}-highlight.png");
            }
            menu.IsSubmenuOpen = false;
        }

        var document = session.Document; int undo = session.UndoCount, redo = session.RedoCount;
        for (int tool = 0; tool < 7; tool++)
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
            var dialog = new ColorPickerWindow(Color.FromRgb(92, 132, 196)) { Owner = this }; Exception? failure = null;
            dialog.Loaded += (_, _) => { try { action(dialog); } catch (Exception ex) { failure = ex; } finally { if (dialog.IsVisible) dialog.Close(); } };
            bool? result = dialog.ShowDialog(); selected = dialog.SelectedColor;
            if (failure is not null) throw new InvalidOperationException("Color picker failed.", failure);
            return result;
        }
        string previous = BrushColor.Text;
        Check(ShowPicker(d => { d.SetColor(Colors.Red); d.Close(); }, out _) != true && BrushColor.Text == previous, "Cancel changed the brush color.");
        Check(ShowPicker(d =>
        {
            d.RgbInputs[0].Text = "256"; Check(!d.ApplyButton.IsEnabled, "Invalid RGB is accepted.");
            d.RgbInputs[0].Text = "64"; Check(d.SelectedColor.R == 64, "RGB edit failed.");
            d.HexInput.Text = "#NOTHEX"; Check(!d.ApplyButton.IsEnabled, "Invalid hex is accepted.");
            d.HexInput.Text = "#FF0000"; d.Hue.Value = 120;
            Check(d.SelectedColor == Colors.Lime, "Hue strip did not update the color.");
            d.Field.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(d), Environment.TickCount, Key.Left) { RoutedEvent = Keyboard.KeyDownEvent });
            Check(d.SelectedColor.R > 0 && d.SelectedColor.G == 255, "Color field keyboard adjustment failed.");
            d.SetColor(Color.FromRgb(92, 132, 196)); d.UpdateLayout();
            var image = new RenderTargetBitmap((int)d.ActualWidth, (int)d.ActualHeight, 96, 96, PixelFormats.Pbgra32); image.Render(d);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using (var output = File.Create(Path.ChangeExtension(reportPath, ".picker.png"))) encoder.Save(output);
            d.ApplyButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }, out var chosen) == true, "Color could not be applied.");
        BrushColor.Text = ColorPickerWindow.Hex(chosen);
        Check(ReadBrush().Red == 92 && ReadBrush().Green == 132 && ReadBrush().Blue == 196, "Chosen color not used by brush.");
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
        double width = Width, height = Height; Width = 1000; Height = 650; LayerInspector.IsExpanded = true; UpdateLayout();
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        UpdateLayout();
        Check(Layers.ActualHeight >= 100, "Layer list disappeared behind the inspector.");
        Check(LayerInspector.TranslatePoint(new Point(0, LayerInspector.ActualHeight), this).Y <= Canvas.TranslatePoint(new Point(0, Canvas.ActualHeight), this).Y + 1, "Inspector exceeds the canvas area.");
        Check(ToolPicker.TranslatePoint(new Point(0, 0), this).X < Canvas.TranslatePoint(new Point(0, 0), this).X, "Tool rail is not left of canvas.");
        Check(ToolOptionsBar.ActualHeight <= 90, "Options bar is not compact at minimum width.");
        var compact = new RenderTargetBitmap((int)ActualWidth, (int)ActualHeight, 96, 96, PixelFormats.Pbgra32); compact.Render(this);
        var compactEncoder = new PngBitmapEncoder(); compactEncoder.Frames.Add(BitmapFrame.Create(compact));
        using (var output = File.Create(Path.ChangeExtension(reportPath, ".compact.png"))) compactEncoder.Save(output);
        Check(BrushOptions.ActualWidth <= Editor.ActualWidth, "Toolbar overflows minimum window width.");
        LayerInspector.IsExpanded = false; Width = width; Height = height; UpdateLayout(); Canvas.Fit();
        var originalBlend = LayerBlend.SelectedItem; LayerBlend.IsDropDownOpen = true;
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        Check(LayerBlend.Template.FindName("PART_Popup", LayerBlend) is System.Windows.Controls.Primitives.Popup { IsOpen: true }, "Blend popup did not open.");
        LayerBlend.SelectedItem = BlendMode.Screen; LayerBlend.IsDropDownOpen = false; LayerBlend.SelectedItem = originalBlend;
        var beforeOpacity = session.Document; LayerOpacitySlider.Value = 45;
        Check(Number(LayerOpacity) == 45 && ReferenceEquals(beforeOpacity, session.Document), "Opacity draft edited the document.");
        ApplyLayer(this, new()); Check(session.ActiveLayer!.Opacity == .45, "Layer opacity slider could not apply.");
        session.Undo(); Refresh();
        Check(((SolidColorBrush)FindResource("Accent")).Color == Color.FromRgb(108, 154, 224), "Blue accent resource changed.");
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow,
            checks = new[] { "stable title and canvas across four tools", "toolbar and rail open actual palette", "all five dark menu popups", "disabled menu contrast", "four direct tool selectors", "contextual options", "slider/number synchronization", "invalid draft and bounds", "settings preserve document/history", "picker cancel and apply", "hex validation", "hue and keyboard color field", "RGB/HSV round trip", "mask presets", "minimum width layout", "RGB validation", "vertical tool rail", "compact options", "visible layer list with expanded inspector", "dark blend popup", "layer opacity draft/apply/undo", "blue theme resource" },
            note = "Hidden WPF control checks; a changed toolbar still needs a user usability check." }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
