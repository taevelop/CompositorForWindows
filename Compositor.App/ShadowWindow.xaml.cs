using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Compositor.Core;

namespace Compositor.App;
public partial class ShadowWindow : Window
{
    private readonly EditorSession session;
    private readonly Document original;
    private readonly Layer layer;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private Color color;
    private bool ready, closed, finished;
    public ShadowWindow(EditorSession session)
    {
        if (session.InTransaction) throw new InvalidOperationException("Finish the active edit first.");
        this.session = session; original = session.Document;
        layer = session.ActiveLayer is { IsGroup: false, IsAdjustment: false } active ? active : throw new InvalidOperationException("Select a pixel layer.");
        InitializeComponent(); SetFields(layer.Effects?.Shadow ?? new());
        session.Begin(); ready = true;
        timer.Tick += (_, _) => Preview(); Closed += (_, _) => CancelEdit();
        Preview();
    }
    private void SetFields(ShadowEffect s)
    {
        AngleSlider.Value = s.Angle; DistanceSlider.Value = s.Distance; BlurSlider.Value = s.Blur; OpacitySlider.Value = s.Opacity * 100;
        color = Color.FromRgb((byte)Math.Round(s.Red * 255), (byte)Math.Round(s.Green * 255), (byte)Math.Round(s.Blue * 255));
        // Keep original normalized RGB precision until a user explicitly chooses another color.
        red = s.Red; green = s.Green; blue = s.Blue;
        Swatch.Background = new SolidColorBrush(color); EffectEnabled.IsChecked = s.IsEnabled;
    }
    private double red, green, blue;
    private ShadowEffect Settings()
    {
        if (new[] { AngleInput, DistanceInput, BlurInput, OpacityInput }.Any(Validation.GetHasError))
            throw new InvalidOperationException("Enter valid numeric values.");
        var prior = layer.Effects?.Shadow;
        bool enabled = EffectEnabled.IsChecked == true;
        bool? flag = enabled == (prior?.IsEnabled ?? true) ? prior?.Enabled : enabled;
        var result = new ShadowEffect(AngleSlider.Value, DistanceSlider.Value, BlurSlider.Value, red, green, blue, OpacitySlider.Value / 100, flag);
        result.Validate(); return result;
    }
    private Document Edited(ShadowEffect? settings) => original.Replace(layer with { Effects = (layer.Effects ?? new()) with { Shadow = settings } });
    private void Queue() { if (!ready || closed) return; timer.Stop(); timer.Start(); }
    private void InputChanged(object sender, TextChangedEventArgs e) => Queue();
    private void SliderChanged(object sender, RoutedPropertyChangedEventArgs<double> e) => Queue();
    private void Changed(object sender, RoutedEventArgs e) { if (ready) Preview(); }
    internal bool Preview()
    {
        timer.Stop(); if (closed) return false;
        try { var s = Settings(); session.Preview(PreviewEnabled.IsChecked == true ? Edited(s) : original); Feedback.Text = "Source pixels stay unchanged."; ApplyButton.IsEnabled = true; return true; }
        catch (Exception error) { session.Preview(original); Feedback.Text = error.Message; ApplyButton.IsEnabled = false; return false; }
    }
    private void PickColor(object sender, RoutedEventArgs e)
    {
        var picker = new ColorPickerWindow(color) { Owner = this };
        if (picker.ShowDialog() != true) return;
        color = picker.SelectedColor; red = color.R / 255d; green = color.G / 255d; blue = color.B / 255d;
        Swatch.Background = new SolidColorBrush(color); Preview();
    }
    internal void SetSettings(ShadowEffect s) { SetFields(s); Preview(); }
    internal void ApplyEdit() { if (!Preview()) return; session.Preview(Edited(Settings())); session.Commit(); finished = true; Close(); }
    internal void RemoveEdit() { if (closed) return; timer.Stop(); session.Preview(Edited(null)); session.Commit(); finished = true; Close(); }
    internal void CancelEdit() { if (closed) return; closed = true; timer.Stop(); if (!finished) session.Cancel(); }
    private void Apply(object sender, RoutedEventArgs e) => ApplyEdit();
    private void Remove(object sender, RoutedEventArgs e) => RemoveEdit();
    private void Reset(object sender, RoutedEventArgs e) => SetSettings(new());
}