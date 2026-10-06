using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Compositor.Core;

namespace Compositor.App;

public partial class ExposureWindow : Window
{
    private readonly EditorSession session;
    private readonly bool pixelEdit;
    private readonly Document original, editing;
    private readonly Layer layer;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private bool initialized, closed, finished;
    public ExposureWindow(EditorSession session, bool create = false, bool pixelEdit = false)
    {
        if (session.InTransaction) throw new InvalidOperationException("Finish the active edit first.");
        this.pixelEdit = pixelEdit; this.session = session; original = session.Document;
        if (create)
        {
            if (original.Layers.Length >= 10_000) throw new InvalidOperationException("The project already has 10,000 layers.");
            var active = session.ActiveLayer;
            layer = Layer.ExposureLayer(original.Width, original.Height, active?.IsGroup == true ? active.Id : active?.ParentId);
            int index = active is null ? original.Layers.Length : original.Layers.IndexOf(active) + 1;
            editing = original with { Layers = original.Layers.Insert(index, layer) };
        }
        else { layer = session.ActiveLayer is { Exposure: not null } selected ? selected : throw new InvalidOperationException("Select an Exposure layer."); editing = original; }
        InitializeComponent(); SetFields(layer.Exposure!);
        session.Begin(); session.ActiveLayerId = layer.Id; initialized = true;
        timer.Tick += (_, _) => Preview(); Closed += (_, _) => CancelEdit();
        Loaded += (_, _) => { if (Owner is not null) { Left = Owner.Left + Math.Max(0, Owner.ActualWidth - ActualWidth - 28); Top = Owner.Top + 100; } ExposureValue.Focus(); };
        Preview();
    }
    private void SetFields(ExposureAdjustment settings)
    {
        ExposureValue.Text = settings.Exposure.ToString(CultureInfo.InvariantCulture);
        OffsetValue.Text = settings.Offset.ToString(CultureInfo.InvariantCulture);
        GammaValue.Text = settings.Gamma.ToString(CultureInfo.InvariantCulture);
    }
    private ExposureAdjustment ReadSettings()
    {
        double Read(TextBox box) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value : throw new InvalidOperationException("Enter finite numbers using a decimal point.");
        var settings = new ExposureAdjustment(Read(ExposureValue), Read(OffsetValue), Read(GammaValue)); settings.Validate(); return settings;
    }
    private void ValueChanged(object sender, TextChangedEventArgs e) { if (!initialized || closed) return; timer.Stop(); timer.Start(); }
    internal bool Preview()
    {
        timer.Stop(); if (closed) return false;
        try
        {
            var settings = ReadSettings(); session.Preview(PreviewEnabled.IsChecked == true ? editing.Replace(layer with { Exposure = settings }) : original);
            Feedback.Text = PreviewEnabled.IsChecked == true ? (pixelEdit ? "Apply changes image pixels; Undo restores them." : "Preview ready") : "Showing original; Apply uses the entered settings."; return true;
        }
        catch (Exception error) { session.Preview(original); Feedback.Text = error.Message; return false; }
    }
    internal void SetSettings(ExposureAdjustment settings) { SetFields(settings); Preview(); }
    internal void SetPreviewVisible(bool enabled) { PreviewEnabled.IsChecked = enabled; Preview(); }
    private void TogglePreview(object sender, RoutedEventArgs e) => Preview();
    private void Reset(object sender, RoutedEventArgs e) => SetSettings(new());
    private void Apply(object sender, RoutedEventArgs e) => ApplyEdit();
    internal void ApplyEdit()
    {
        if (closed || !Preview()) return;
        try { session.Preview(editing.Replace(layer with { Exposure = ReadSettings() })); session.Commit(); finished = true; Close(); }
        catch (Exception error) { session.Preview(original); Feedback.Text = error.Message; }
    }
    internal void CancelEdit()
    {
        if (closed) return; closed = true; timer.Stop(); if (!finished) session.Cancel();
    }
}
