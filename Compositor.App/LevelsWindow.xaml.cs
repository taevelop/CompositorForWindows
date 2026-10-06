using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Compositor.Core;

namespace Compositor.App;

public partial class LevelsWindow : Window
{
    private readonly EditorSession session;
    private readonly bool pixelEdit;
    private readonly Document original, editing;
    private readonly Layer layer;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private LevelsAdjustment settings;
    private bool initialized, updating, closed, finished;
    public LevelsWindow(EditorSession session, bool create = false, bool pixelEdit = false)
    {
        if (session.InTransaction) throw new InvalidOperationException("Finish the active edit first.");
        this.pixelEdit = pixelEdit; this.session = session; original = session.Document;
        if (create)
        {
            if (original.Layers.Length >= 10_000) throw new InvalidOperationException("The project already has 10,000 layers.");
            var active = session.ActiveLayer;
            layer = Layer.LevelsLayer(original.Width, original.Height, active?.IsGroup == true ? active.Id : active?.ParentId);
            int index = active is null ? original.Layers.Length : original.Layers.IndexOf(active) + 1;
            editing = original with { Layers = original.Layers.Insert(index, layer) };
        }
        else { layer = session.ActiveLayer is { Levels: not null } selected ? selected : throw new InvalidOperationException("Select a Levels layer."); editing = original; }
        settings = layer.Levels!;
        InitializeComponent(); ChannelPicker.ItemsSource = Enum.GetValues<LevelsChannel>(); SetFields();
        session.Begin(); session.ActiveLayerId = layer.Id; initialized = true;
        timer.Tick += (_, _) => Preview(); Closed += (_, _) => CancelEdit();
        Loaded += (_, _) => { if (Owner is not null) { Left = Owner.Left + Math.Max(0, Owner.ActualWidth - ActualWidth - 28); Top = Owner.Top + 40; } BlackValue.Focus(); };
        Preview();
    }
    private void SetFields()
    {
        updating = true;
        try
        {
            ChannelPicker.SelectedItem = settings.Channel; var range = settings.Range(settings.Channel);
            BlackValue.Text = range.Black.ToString(CultureInfo.InvariantCulture); GammaValue.Text = range.Gamma.ToString(CultureInfo.InvariantCulture);
            WhiteValue.Text = range.White.ToString(CultureInfo.InvariantCulture); OutputBlackValue.Text = range.OutputBlack.ToString(CultureInfo.InvariantCulture);
            OutputWhiteValue.Text = range.OutputWhite.ToString(CultureInfo.InvariantCulture);
        }
        finally { updating = false; }
    }
    private void ReadFields()
    {
        double Read(TextBox box) => double.TryParse(box.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && double.IsFinite(value)
            ? value : throw new InvalidOperationException("Enter finite numbers using a decimal point.");
        var range = new LevelRange(Read(BlackValue), Read(GammaValue), Read(WhiteValue), Read(OutputBlackValue), Read(OutputWhiteValue));
        range.Validate(); settings = settings.WithRange(settings.Channel, range);
    }
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
            ReadFields(); session.Preview(PreviewEnabled.IsChecked == true ? editing.Replace(layer with { Levels = settings }) : original);
            Feedback.Text = PreviewEnabled.IsChecked == true ? (pixelEdit ? "Apply changes image pixels; Undo restores them." : "Preview ready") : "Showing original; Apply uses the entered settings."; return true;
        }
        catch (Exception error) { session.Preview(original); Feedback.Text = error.Message; return false; }
    }
    internal void SetSettings(LevelsAdjustment value) { value.Validate(); settings = value; SetFields(); Preview(); }
    internal void SetPreviewVisible(bool enabled) { PreviewEnabled.IsChecked = enabled; Preview(); }
    private void TogglePreview(object sender, RoutedEventArgs e) => Preview();
    private void ResetChannel(object sender, RoutedEventArgs e) => SetSettings(settings.WithRange(settings.Channel, new()));
    private void ResetAll(object sender, RoutedEventArgs e) => SetSettings(new());
    private void Apply(object sender, RoutedEventArgs e) => ApplyEdit();
    internal void ApplyEdit()
    {
        if (closed || !Preview()) return;
        try { session.Preview(editing.Replace(layer with { Levels = settings })); session.Commit(); finished = true; Close(); }
        catch (Exception error) { session.Preview(original); Feedback.Text = error.Message; }
    }
    internal void CancelEdit()
    {
        if (closed) return; closed = true; timer.Stop(); if (!finished) session.Cancel();
    }
}
