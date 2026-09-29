using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Compositor.Core;

namespace Compositor.App;

public partial class CurvesWindow : Window
{
    private readonly EditorSession session;
    private readonly Document original, editing;
    private readonly Layer layer;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private CurvesAdjustment settings;
    private bool initialized, updating, closed, finished;
    public CurvesWindow(EditorSession session, bool create = false)
    {
        if (session.InTransaction) throw new InvalidOperationException("Finish the active edit first.");
        this.session = session; original = session.Document;
        if (create)
        {
            if (original.Layers.Length >= 10_000) throw new InvalidOperationException("The project already has 10,000 layers.");
            var active = session.ActiveLayer;
            layer = Layer.CurvesLayer(original.Width, original.Height, active?.IsGroup == true ? active.Id : active?.ParentId);
            int index = active is null ? original.Layers.Length : original.Layers.IndexOf(active) + 1;
            editing = original with { Layers = original.Layers.Insert(index, layer) };
        }
        else { layer = session.ActiveLayer is { Curves: not null } selected ? selected : throw new InvalidOperationException("Select a Curves layer."); editing = original; }
        settings = layer.Curves!;
        InitializeComponent(); Graph.FlushPending = FlushPending; Graph.Edited += GraphEdited; ChannelPicker.ItemsSource = Enum.GetValues<LevelsChannel>(); SetFields();
        session.Begin(); session.ActiveLayerId = layer.Id; initialized = true;
        timer.Tick += (_, _) => Preview(); Closed += (_, _) => CancelEdit();
        Loaded += (_, _) => { if (Owner is not null) { Left = Owner.Left + Math.Max(0, Owner.ActualWidth - ActualWidth - 28); Top = Owner.Top + 40; } Graph.Focus(); };
        Preview();
    }
    private void SetFields()
    {
        updating = true;
        try
        {
            ChannelPicker.SelectedItem = settings.Channel; Graph.Curve = settings.Curve(settings.Channel);
            var points = Graph.Curve.Points;
            PointPicker.ItemsSource = points.Select((p, i) => $"Point {i + 1}").ToArray();
            PointPicker.SelectedIndex = Graph.SelectedIndex;
            var point = points[Graph.SelectedIndex]; InputValue.Text = point.X.ToString(CultureInfo.InvariantCulture); OutputValue.Text = point.Y.ToString(CultureInfo.InvariantCulture);
            InputValue.IsReadOnly = Graph.SelectedIndex == 0 || Graph.SelectedIndex == points.Length - 1;
            DeleteButton.IsEnabled = !InputValue.IsReadOnly;
        }
        finally { updating = false; }
    }
    private void ReadFields()
    {
        double Read(TextBox box) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value : throw new InvalidOperationException("Enter finite numbers using a decimal point.");
        var points = settings.Curve(settings.Channel).Points.ToArray();
        points[Graph.SelectedIndex] = new(Read(InputValue), Read(OutputValue));
        var curve = new ToneCurve(points); settings = settings.WithCurve(settings.Channel, curve); Graph.Curve = curve;
    }
    private bool FlushPending()
    {
        try { ReadFields(); return true; }
        catch (Exception error) { timer.Stop(); session.Preview(original); Feedback.Text = error.Message; return false; }
    }
    private void GraphEdited()
    {
        settings = settings.WithCurve(settings.Channel, Graph.Curve); SetFields(); Schedule();
    }
    private void PointChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || updating || closed) return;
        int index = PointPicker.SelectedIndex;
        if (!Graph.SelectPoint(index)) { updating = true; PointPicker.SelectedIndex = Graph.SelectedIndex; updating = false; }
    }
    private void Schedule() { timer.Stop(); timer.Start(); }
    private void AddPoint(object sender, RoutedEventArgs e)
    {
        if (!FlushPending()) return;
        var curve = settings.Curve(settings.Channel);
        if (curve.Points.Length == 32) { Feedback.Text = "A curve supports at most 32 points."; return; }
        int i = Math.Min(Graph.SelectedIndex, curve.Points.Length - 2);
        double x = (curve.Points[i].X + curve.Points[i + 1].X) / 2;
        if (!Graph.BeginPoint(x, curve.Value(x), insert: true)) Feedback.Text = "These inputs are too close to add another point.";
        Graph.EndPoint(true);
    }
    private void DeletePoint(object sender, RoutedEventArgs e) => Graph.DeletePoint();
    private void ChannelChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!initialized || updating || closed) return;
        timer.Stop();
        try { ReadFields(); settings = settings with { Channel = (LevelsChannel)ChannelPicker.SelectedItem }; SetFields(); Preview(); }
        catch (Exception error)
        {
            updating = true; ChannelPicker.SelectedItem = settings.Channel; updating = false;
            session.Preview(original); Feedback.Text = error.Message;
        }
    }
    private void ValueChanged(object sender, TextChangedEventArgs e) { if (!initialized || updating || closed) return; timer.Stop(); timer.Start(); }
    internal bool Preview()
    {
        timer.Stop(); if (closed) return false;
        try
        {
            ReadFields(); session.Preview(PreviewEnabled.IsChecked == true ? editing.Replace(layer with { Curves = settings }) : original);
            Feedback.Text = PreviewEnabled.IsChecked == true ? "Preview ready" : "Showing original; Apply uses the entered settings."; return true;
        }
        catch (Exception error) { session.Preview(original); Feedback.Text = error.Message; return false; }
    }
    internal void SetSettings(CurvesAdjustment value) { value.Validate(); settings = value; SetFields(); Preview(); }
    internal void SetPreviewVisible(bool enabled) { PreviewEnabled.IsChecked = enabled; Preview(); }
    private void TogglePreview(object sender, RoutedEventArgs e) => Preview();
    private void ResetChannel(object sender, RoutedEventArgs e) => SetSettings(settings.WithCurve(settings.Channel, ToneCurve.Identity));
    private void ResetAll(object sender, RoutedEventArgs e) => SetSettings(new());
    private void Apply(object sender, RoutedEventArgs e) => ApplyEdit();
    internal void ApplyEdit()
    {
        if (closed || !Preview()) return;
        session.Preview(editing.Replace(layer with { Curves = settings })); session.Commit(); finished = true; Close();
    }
    internal void CancelEdit()
    {
        if (closed) return; closed = true; timer.Stop(); if (!finished) session.Cancel();
    }
}
