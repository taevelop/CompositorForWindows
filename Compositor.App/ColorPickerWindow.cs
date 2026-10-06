using System.Globalization;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Compositor.App;

internal sealed class ColorPickerWindow : Window
{
    internal readonly ColorField Field = new() { Width = 256, Height = 256, Margin = new Thickness(3, 6, 3, 6) };
    internal readonly Slider Hue = new() { Minimum = 0, Maximum = 360, SmallChange = 1, LargeChange = 15, Margin = new Thickness(3, 8, 3, 12) };
    internal readonly TextBox HexInput = new() { Width = 110 };
    internal readonly Button ApplyButton = new() { Content = "Use color", IsDefault = true, MinWidth = 100 };
    private readonly Border preview = new() { Width = 64, Height = 40, Margin = new Thickness(3), BorderBrush = Brushes.White, BorderThickness = new Thickness(1) };
    private readonly TextBlock error = new() { Foreground = Brushes.Salmon, Text = "Enter six hex digits, e.g. #6C9AE0", Visibility = Visibility.Collapsed, Margin = new Thickness(3) };
    internal readonly TextBox[] RgbInputs = Enumerable.Range(0, 3).Select(_ => new TextBox { Width = 78, Padding = new Thickness(4, 2, 4, 2) }).ToArray();
    private bool updating;
    public Color SelectedColor => Field.SelectedColor;

    public ColorPickerWindow(Color original)
    {
        Style = (Style)Application.Current.FindResource(typeof(Window));
        Title = "Foreground color"; SizeToContent = SizeToContent.WidthAndHeight;
        ResizeMode = ResizeMode.NoResize; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new StackPanel { Margin = new Thickness(18) };
        var textStyle = new Style(typeof(TextBlock), (Style)Application.Current.FindResource(typeof(TextBlock)));
        textStyle.Setters.Add(new Setter(TextBlock.ForegroundProperty, Brushes.White)); root.Resources.Add(typeof(TextBlock), textStyle);
        root.Children.Add(new TextBlock { Text = "Foreground color", FontSize = 16, FontWeight = FontWeights.SemiBold, Margin = new Thickness(3, 0, 0, 12) });
        var body = new StackPanel { Orientation = Orientation.Horizontal };
        body.Children.Add(Field);
        Hue.Style = (Style)Application.Current.FindResource("HueSlider"); Hue.Width = 28; Hue.Height = 256; Hue.Margin = new Thickness(8, 6, 12, 6);
        AutomationProperties.SetName(Hue, "Hue"); body.Children.Add(Hue);
        var details = new StackPanel { Width = 152, Margin = new Thickness(0, 6, 0, 0) };
        var swatches = new StackPanel { Orientation = Orientation.Horizontal };
        swatches.Children.Add(new StackPanel { Children = { new TextBlock { Text = "Current" }, new Border { Width = 64, Height = 40, Margin = new Thickness(3), Background = new SolidColorBrush(original), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1) } } });
        swatches.Children.Add(new StackPanel { Children = { new TextBlock { Text = "New" }, preview } }); details.Children.Add(swatches);
        ApplyButton.Style = (Style)Application.Current.FindResource("CompactButton"); ApplyButton.Background = (Brush)Application.Current.FindResource("Accent"); ApplyButton.Foreground = Brushes.Black;
        details.Children.Add(ApplyButton);
        details.Children.Add(new Button { Content = "Cancel", IsCancel = true, Style = (Style)Application.Current.FindResource("CompactButton") });
        for (int channel = 0; channel < 3; channel++)
        {
            var row = new DockPanel { Margin = new Thickness(3, channel == 0 ? 12 : 0, 3, 0) };
            row.Children.Add(new TextBlock { Text = new[] { "R", "G", "B" }[channel], Width = 24 }); row.Children.Add(RgbInputs[channel]);
            AutomationProperties.SetName(RgbInputs[channel], new[] { "Red", "Green", "Blue" }[channel]); details.Children.Add(row);
        }
        var hexRow = new DockPanel { Margin = new Thickness(3, 0, 3, 0) }; hexRow.Children.Add(new TextBlock { Text = "#", Width = 24 }); hexRow.Children.Add(HexInput); details.Children.Add(hexRow);
        AutomationProperties.SetName(HexInput, "Hex RGB color"); body.Children.Add(details); root.Children.Add(body);
        var samples = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
        foreach (string hex in new[] { "#000000", "#FFFFFF", "#EF4444", "#F59E0B", "#FDE047", "#22C55E", "#38BDF8", "#6C9AE0", "#8B5CF6", "#EC4899" })
        {
            TryHex(hex, out var color);
            var button = new Button { Width = 26, Height = 26, Padding = new Thickness(0), Margin = new Thickness(2), Background = new SolidColorBrush(color), ToolTip = hex };
            AutomationProperties.SetName(button, "Color " + hex); button.Click += (_, _) => SetColor(color); samples.Children.Add(button);
        }
        root.Children.Add(samples); root.Children.Add(error);
        root.Children.Add(new TextBlock { Text = "Drag to choose · Arrow keys adjust the field · Shift for larger steps", FontSize = 11, Foreground = Brushes.LightGray, Margin = new Thickness(3, 8, 0, 0) });
        Content = root;
        foreach (var input in RgbInputs) input.TextChanged += (_, _) =>
        {
            if (updating) return;
            bool valid = byte.TryParse(RgbInputs[0].Text, out byte r) & byte.TryParse(RgbInputs[1].Text, out byte g) & byte.TryParse(RgbInputs[2].Text, out byte b);
            ApplyButton.IsEnabled = valid; error.Text = "RGB channels must be whole numbers from 0 to 255."; error.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            if (valid) { Field.SetColor(Color.FromRgb(r, g, b)); Refresh(false); }
        };

        Hue.ValueChanged += (_, _) => { if (!updating) { Field.Hue = Hue.Value; Field.InvalidateVisual(); Refresh(false); } };
        Field.Changed += () => Refresh(false);
        HexInput.TextChanged += (_, _) =>
        {
            if (updating) return;
            bool valid = TryHex(HexInput.Text, out var color); error.Text = "Enter six hex digits, e.g. #6C9AE0";
            ApplyButton.IsEnabled = valid; error.Visibility = valid ? Visibility.Collapsed : Visibility.Visible;
            if (valid) { Field.SetColor(color); Refresh(true); }
        };
        ApplyButton.Click += (_, _) => { if (TryHex(HexInput.Text, out _)) DialogResult = true; };
        SetColor(original);
    }
    internal void SetColor(Color color) { Field.SetColor(color); Refresh(false); }
    private void Refresh(bool keepDraft)
    {
        updating = true; Hue.Value = Field.Hue; preview.Background = new SolidColorBrush(SelectedColor);
        if (!keepDraft) HexInput.Text = Hex(SelectedColor);
        RgbInputs[0].Text = SelectedColor.R.ToString(); RgbInputs[1].Text = SelectedColor.G.ToString(); RgbInputs[2].Text = SelectedColor.B.ToString();
        ApplyButton.IsEnabled = true; error.Visibility = Visibility.Collapsed; updating = false;
    }
    internal static string Hex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
    internal static bool TryHex(string text, out Color color)
    {
        text = text.Trim(); if (text.StartsWith('#')) text = text[1..]; color = Colors.Black;
        if (text.Length != 6 || !uint.TryParse(text, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint n)) return false;
        color = Color.FromRgb((byte)(n >> 16), (byte)(n >> 8), (byte)n); return true;
    }
}

internal sealed class ColorField : FrameworkElement
{
    public double Hue { get; set; }
    public double Saturation { get; private set; }
    public double Brightness { get; private set; }
    public Color SelectedColor => FromHsv(Hue, Saturation, Brightness);
    public event Action? Changed;
    public ColorField() { Focusable = true; Cursor = Cursors.Cross; AutomationProperties.SetName(this, "Saturation and brightness"); }
    internal void SetColor(Color color)
    {
        double r = color.R / 255.0, g = color.G / 255.0, b = color.B / 255.0;
        double max = Math.Max(r, Math.Max(g, b)), min = Math.Min(r, Math.Min(g, b)), delta = max - min;
        Brightness = max; Saturation = max == 0 ? 0 : delta / max;
        if (delta > 0) Hue = ((max == r ? (g - b) / delta : max == g ? (b - r) / delta + 2 : (r - g) / delta + 4) * 60 + 360) % 360;
        InvalidateVisual();
    }
    internal static Color FromHsv(double hue, double saturation, double brightness)
    {
        double h = (hue % 360) / 60, c = brightness * saturation, x = c * (1 - Math.Abs(h % 2 - 1)), m = brightness - c;
        (double r, double g, double b) = h switch { < 1 => (c, x, 0d), < 2 => (x, c, 0d), < 3 => (0d, c, x), < 4 => (0d, x, c), < 5 => (x, 0d, c), _ => (c, 0d, x) };
        return Color.FromRgb((byte)Math.Round((r + m) * 255), (byte)Math.Round((g + m) * 255), (byte)Math.Round((b + m) * 255));
    }
    protected override void OnRender(DrawingContext dc)
    {
        var bounds = new Rect(0, 0, ActualWidth, ActualHeight);
        dc.DrawRectangle(new LinearGradientBrush(Colors.White, FromHsv(Hue, 1, 1), 0), null, bounds);
        dc.DrawRectangle(new LinearGradientBrush(Colors.Transparent, Colors.Black, 90), new Pen(IsKeyboardFocused ? Brushes.White : Brushes.Gray, 1), bounds);
        var point = new Point(Math.Clamp(Saturation * ActualWidth, 6, ActualWidth - 6), Math.Clamp((1 - Brightness) * ActualHeight, 6, ActualHeight - 6));
        dc.DrawEllipse(null, new Pen(Brushes.Black, 3), point, 5, 5); dc.DrawEllipse(null, new Pen(Brushes.White, 1), point, 5, 5);
    }
    private void Pick(Point point)
    {
        Saturation = Math.Clamp(point.X / ActualWidth, 0, 1); Brightness = 1 - Math.Clamp(point.Y / ActualHeight, 0, 1);
        InvalidateVisual(); Changed?.Invoke();
    }
    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e) { Focus(); CaptureMouse(); Pick(e.GetPosition(this)); e.Handled = true; }
    protected override void OnMouseMove(MouseEventArgs e) { if (IsMouseCaptured && e.LeftButton == MouseButtonState.Pressed) Pick(e.GetPosition(this)); }
    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e) { if (IsMouseCaptured) { Pick(e.GetPosition(this)); ReleaseMouseCapture(); e.Handled = true; } }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        double step = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? .1 : .01;
        switch (e.Key) { case Key.Left: Saturation -= step; break; case Key.Right: Saturation += step; break; case Key.Up: Brightness += step; break; case Key.Down: Brightness -= step; break; default: base.OnKeyDown(e); return; }
        Saturation = Math.Clamp(Saturation, 0, 1); Brightness = Math.Clamp(Brightness, 0, 1); InvalidateVisual(); Changed?.Invoke(); e.Handled = true;
    }
}
