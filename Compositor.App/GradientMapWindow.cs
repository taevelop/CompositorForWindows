using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Compositor.Core;

namespace Compositor.App;

public sealed class GradientMapWindow : Window
{
    private readonly EditorSession session;
    private readonly bool pixelEdit;
    private readonly Document original, editing;
    private readonly Layer layer;
    private GradientMapAdjustment settings;
    private bool ready, closed, finished;
    internal readonly CheckBox Reverse = new() { Content = "Reverse gradient", Margin = new(4, 12, 4, 8) };
    internal readonly CheckBox PreviewEnabled = new() { Content = "Preview changes", IsChecked = true, Margin = new(4, 12, 4, 8) };
    internal readonly Button ShadowsButton = new() { Content = "Shadows · choose color…" };
    internal readonly Button HighlightsButton = new() { Content = "Highlights · choose color…" };
    private readonly Border ramp = new() { Height = 44, Margin = new(4, 12, 4, 4), CornerRadius = new(4) };
    private readonly Border shadowSwatch = new() { Width = 42, Height = 32, Margin = new(4) };
    private readonly Border highlightSwatch = new() { Width = 42, Height = 32, Margin = new(4) };
    private readonly TextBlock feedback = new() { Margin = new(4), TextWrapping = TextWrapping.Wrap, MinHeight = 32 };

    public GradientMapWindow(EditorSession session, bool create, bool pixelEdit = false)
    {
        if (session.InTransaction) throw new InvalidOperationException("Finish the active edit first.");
        this.pixelEdit=pixelEdit; this.session = session; original = session.Document;
        if (create)
        {
            var active = session.ActiveLayer;
            layer = Layer.GradientMapLayer(original.Width, original.Height, active?.IsGroup == true ? active.Id : active?.ParentId);
            int index = active is null ? original.Layers.Length : original.Layers.IndexOf(active) + 1;
            editing = original with { Layers = original.Layers.Insert(index, layer) };
            editing.Validate();
        }
        else
        {
            layer = session.ActiveLayer ?? throw new InvalidOperationException("Select a Gradient Map layer.");
            if (layer.GradientMap is null) throw new InvalidOperationException("Select a Gradient Map layer.");
            editing = original;
        }
        settings = layer.GradientMap!;
        Title = "Gradient Map"; Width = 440; Height = 405; MinWidth = 350; MinHeight = 330;
        Style = (Style)Application.Current.FindResource(typeof(Window));
        ShowInTaskbar = false; WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Reverse.Foreground = PreviewEnabled.Foreground = (Brush)FindResource("Ink");
        var root = new DockPanel { Margin = new(18) }; Content = root;
        var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
        footer.Children.Add(PreviewEnabled); footer.Children.Add(feedback);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        footer.Children.Add(actions);
        void AddButton(string text, Action action, bool cancel = false)
        {
            var button = new Button { Content = text, IsCancel = cancel, Style = (Style)FindResource("CompactButton") };
            button.Click += (_, _) => action(); actions.Children.Add(button);
        }
        AddButton("Reset", () => SetSettings(new())); AddButton("Cancel", Close, true); AddButton("Apply", ApplyEdit);
        var body = new StackPanel();
        root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        body.Children.Add(new TextBlock { Text = "Map image brightness to two colors", Margin = new(4, 0, 4, 10) });
        void ColorRow(Button button, Border swatch)
        {
            var row = new DockPanel(); DockPanel.SetDock(swatch, Dock.Right); row.Children.Add(swatch); row.Children.Add(button); body.Children.Add(row);
        }
        ColorRow(ShadowsButton, shadowSwatch); ColorRow(HighlightsButton, highlightSwatch);
        body.Children.Add(ramp); body.Children.Add(Reverse);
        ShadowsButton.Click += (_, _) => PickColor(false); HighlightsButton.Click += (_, _) => PickColor(true);
        Reverse.Click += (_, _) => Preview(); PreviewEnabled.Click += (_, _) => Preview();
        session.Begin(); session.ActiveLayerId = layer.Id; ready = true; Closed += (_, _) => CancelEdit();
        SetSettings(settings);
    }
    private static Color DisplayColor(AdjustmentColor c) => Color.FromRgb(
        (byte)Math.Round(c.Red * 255), (byte)Math.Round(c.Green * 255), (byte)Math.Round(c.Blue * 255));
    private void PickColor(bool highlights)
    {
        var picker = new ColorPickerWindow(DisplayColor(highlights ? settings.Highlights : settings.Shadows)) { Owner = this };
        if (picker.ShowDialog() != true) return;
        var c = picker.SelectedColor; var selected = new AdjustmentColor(c.R / 255d, c.G / 255d, c.B / 255d);
        settings = highlights ? settings with { Highlights = selected } : settings with { Shadows = selected };
        Preview();
    }
    internal void SetSettings(GradientMapAdjustment value)
    {
        value.Validate(); settings = value; Reverse.IsChecked = value.Reversed; Preview();
    }
    internal bool Preview()
    {
        if (!ready || closed) return false;
        try
        {
        settings = settings with { Reversed = Reverse.IsChecked == true };
        settings.Validate();
        var dark = DisplayColor(settings.Shadows); var light = DisplayColor(settings.Highlights);
        shadowSwatch.Background = new SolidColorBrush(dark); highlightSwatch.Background = new SolidColorBrush(light);
        ramp.Background = new LinearGradientBrush(settings.Reversed ? light : dark, settings.Reversed ? dark : light, 0);
        session.Preview(PreviewEnabled.IsChecked == true ? editing.Replace(layer with { GradientMap = settings }) : original);
        feedback.Text = pixelEdit ? "Apply changes image pixels; Undo restores them." : "Source pixels stay unchanged."; return true;
        }
        catch (Exception error) { session.Preview(original); feedback.Text=error.Message; return false; }
    }
    internal void ApplyEdit()
    {
        if (!Preview()) return;
        try { session.Preview(editing.Replace(layer with { GradientMap = settings })); session.Commit(); finished = true; Close(); } catch(Exception error){session.Preview(original);feedback.Text=error.Message;}
    }
    internal void CancelEdit()
    {
        if (closed) return; closed = true; if (ready && !finished) session.Cancel();
    }
}
