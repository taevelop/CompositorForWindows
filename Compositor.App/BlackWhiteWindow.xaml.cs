using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Compositor.Core;

namespace Compositor.App;

public partial class BlackWhiteWindow : Window
{
    private readonly EditorSession session;
    private readonly bool pixelEdit;
    private readonly Document original, editing;
    private readonly Layer layer;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private bool initialized, closed, finished;
    public BlackWhiteWindow(EditorSession session, bool create = false, bool pixelEdit = false)
    {
        if (session.InTransaction) throw new InvalidOperationException("Finish the active edit first.");
        this.pixelEdit = pixelEdit; this.session = session; original = session.Document;
        if (create)
        {
            if (original.Layers.Length >= 10_000) throw new InvalidOperationException("The project already has 10,000 layers.");
            var active = session.ActiveLayer;
            layer = Layer.BlackWhiteLayer(original.Width, original.Height, active?.IsGroup == true ? active.Id : active?.ParentId);
            int index = active is null ? original.Layers.Length : original.Layers.IndexOf(active) + 1;
            editing = original with { Layers = original.Layers.Insert(index, layer) };
        }
        else { layer = session.ActiveLayer is { BlackWhite: not null } selected ? selected : throw new InvalidOperationException("Select an BlackWhite layer."); editing = original; }
        InitializeComponent(); SetFields(layer.BlackWhite!);
        session.Begin(); session.ActiveLayerId = layer.Id; initialized = true;
        timer.Tick += (_, _) => Preview(); Closed += (_, _) => CancelEdit();
        Loaded += (_, _) => { if (Owner is not null) { Left = Owner.Left + Math.Max(0, Owner.ActualWidth - ActualWidth - 28); Top = Owner.Top + 100; } RedsValue.Focus(); };
        Preview();
    }
    private void SetFields(BlackWhiteAdjustment settings)
    {
RedsSlider.Value = settings.Reds;
        YellowsSlider.Value = settings.Yellows;
        GreensSlider.Value = settings.Greens;
        CyansSlider.Value = settings.Cyans;
        BluesSlider.Value = settings.Blues;
        MagentasSlider.Value = settings.Magentas;
        TintHueSlider.Value = settings.TintHue;
        TintSaturationSlider.Value = settings.TintSaturation;
        Tint.IsChecked = settings.Tint;
    }
    private BlackWhiteAdjustment ReadSettings()
    {
        if (new[] { RedsValue, YellowsValue, GreensValue, CyansValue, BluesValue, MagentasValue, TintHueValue, TintSaturationValue }.Any(Validation.GetHasError)) throw new InvalidOperationException("Enter valid numbers.");
        var settings = new BlackWhiteAdjustment(RedsSlider.Value, YellowsSlider.Value, GreensSlider.Value, CyansSlider.Value, BluesSlider.Value, MagentasSlider.Value, Tint.IsChecked == true, TintHueSlider.Value, TintSaturationSlider.Value);
        settings.Validate(); return settings;
    }
    private void ValueChanged(object sender, TextChangedEventArgs e) { if (!initialized || closed) return; timer.Stop(); timer.Start(); }
    internal bool Preview()
    {
        timer.Stop(); if (closed) return false;
        try
        {
            var settings = ReadSettings(); session.Preview(PreviewEnabled.IsChecked == true ? editing.Replace(layer with { BlackWhite = settings }) : original);
            Feedback.Text = PreviewEnabled.IsChecked == true ? (pixelEdit ? "Apply changes image pixels; Undo restores them." : "Preview ready") : "Showing original; Apply uses the entered settings."; return true;
        }
        catch (Exception error) { session.Preview(original); Feedback.Text = error.Message; return false; }
    }
    internal void SetSettings(BlackWhiteAdjustment settings) { SetFields(settings); Preview(); }
    internal void SetPreviewVisible(bool enabled) { PreviewEnabled.IsChecked = enabled; Preview(); }
    private void TogglePreview(object sender, RoutedEventArgs e) => Preview();
    private void Reset(object sender, RoutedEventArgs e) => SetSettings(new());
    private void Apply(object sender, RoutedEventArgs e) => ApplyEdit();
    internal void ApplyEdit()
    {
        if (closed || !Preview()) return;
        try { session.Preview(editing.Replace(layer with { BlackWhite = ReadSettings() })); session.Commit(); finished = true; Close(); }
        catch (Exception error) { session.Preview(original); Feedback.Text = error.Message; }
    }
    internal void CancelEdit()
    {
        if (closed) return; closed = true; timer.Stop(); if (!finished) session.Cancel();
    }
}
