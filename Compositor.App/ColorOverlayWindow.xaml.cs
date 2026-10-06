using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using Compositor.Core;

namespace Compositor.App;

public partial class ColorOverlayWindow : Window
{
    private readonly EditorSession session;
    private readonly Document original;
    private readonly Layer layer;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private bool initialized, closed, finished;
    public ColorOverlayWindow(EditorSession session)
    {
        if (session.InTransaction) throw new InvalidOperationException("Finish the active edit first.");
        this.session = session; original = session.Document;
        layer = session.ActiveLayer is { IsGroup: false, IsAdjustment: false } selected ? selected : throw new InvalidOperationException("Select a pixel layer.");
        InitializeComponent(); SetFields(layer.Effects?.ColorOverlay ?? new(1, 1, 1));
        session.Begin(); initialized = true; timer.Tick += (_, _) => Preview(); Closed += (_, _) => CancelEdit();
        Loaded += (_, _) => { if (Owner is not null) { Left = Owner.Left + Math.Max(0, Owner.ActualWidth - ActualWidth - 28); Top = Owner.Top + 50; } RedValue.Focus(); };
        Preview();
    }
    private void SetFields(ColorOverlayEffect settings)
    {
        RedValue.Text = settings.Red.ToString(CultureInfo.InvariantCulture); GreenValue.Text = settings.Green.ToString(CultureInfo.InvariantCulture);
        BlueValue.Text = settings.Blue.ToString(CultureInfo.InvariantCulture); OpacityValue.Text = settings.Opacity.ToString(CultureInfo.InvariantCulture);
        EffectEnabled.IsChecked = settings.IsEnabled;
    }
    private ColorOverlayEffect ReadSettings()
    {
        double Read(TextBox box) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value : throw new InvalidOperationException("Enter finite numbers using a decimal point.");
        var prior = layer.Effects?.ColorOverlay;
        bool enabled = EffectEnabled.IsChecked == true;
        // Keep the optional Mac flag unchanged on no-op edits, including missing/null == enabled.
        bool? flag = enabled == (prior?.IsEnabled ?? true) ? prior?.Enabled : enabled;
        var settings = new ColorOverlayEffect(Read(RedValue), Read(GreenValue), Read(BlueValue), Read(OpacityValue), flag);
        settings.Validate(); return settings;
    }
    private Document Edited(ColorOverlayEffect settings) => original.Replace(layer with { Effects = (layer.Effects ?? new()) with { ColorOverlay = settings } });
    private void ValueChanged(object sender, TextChangedEventArgs e) { if (!initialized || closed) return; timer.Stop(); timer.Start(); }
    internal bool Preview()
    {
        timer.Stop(); if (closed) return false;
        try
        {
            var settings = ReadSettings();
            ColorSwatch.Background = new SolidColorBrush(Color.FromRgb((byte)Math.Round(settings.Red * 255), (byte)Math.Round(settings.Green * 255), (byte)Math.Round(settings.Blue * 255)));
            session.Preview(PreviewEnabled.IsChecked == true ? Edited(settings) : original);
            Feedback.Text = PreviewEnabled.IsChecked == true ? "Preview ready" : "Showing original; Apply uses the entered settings."; return true;
        }
        catch (Exception error) { session.Preview(original); Feedback.Text = error.Message; return false; }
    }
    internal void SetSettings(ColorOverlayEffect settings) { SetFields(settings); Preview(); }
    internal void SetPreviewVisible(bool enabled) { PreviewEnabled.IsChecked = enabled; Preview(); }
    private void TogglePreview(object sender, RoutedEventArgs e) => Preview();
    private void Reset(object sender, RoutedEventArgs e) => SetSettings(new(1, 1, 1));
    private void Apply(object sender, RoutedEventArgs e) => ApplyEdit();
    internal void ApplyEdit()
    {
        if (closed || !Preview()) return;
        session.Preview(Edited(ReadSettings())); session.Commit(); finished = true; Close();
    }
    private void Remove(object sender, RoutedEventArgs e) => RemoveEdit();
    internal void RemoveEdit()
    {
        if (closed) return; timer.Stop();
        session.Preview(original.Replace(layer with { Effects = layer.Effects is null ? null : layer.Effects with { ColorOverlay = null } })); session.Commit(); finished = true; Close();
    }
    internal void CancelEdit()
    {
        if (closed) return; closed = true; timer.Stop(); if (!finished) session.Cancel();
    }
}
