using System.Windows;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public partial class ColorAdjustmentWindow : Window
{
    private readonly EditorSession session;
    private readonly Document original;
    private readonly Layer layer;
    private Raster? result;
    private CancellationTokenSource? pending;
    private bool initialized, closed, finished, settingValues;
    private long request;
    private string? previewError;
    internal Task PendingPreview { get; private set; } = Task.CompletedTask;

    public ColorAdjustmentWindow(EditorSession session)
    {
        if (session.InTransaction || session.EditMask || session.ActiveLayer is not { IsGroup: false } selected)
            throw new InvalidOperationException("Select an image layer and Edit image before adjusting colors.");
        this.session = session; original = session.Document; layer = selected; result = layer.Pixels;
        InitializeComponent(); TargetName.Text = layer.Name;
        session.Begin(); initialized = true;
        Closed += (_, _) => CancelEdit();
        Loaded += (_, _) =>
        {
            // Keep most of the owner's canvas visible while inspecting the preview.
            if (Owner is not null) { Left = Owner.Left + Math.Max(0, Owner.ActualWidth - ActualWidth - 28); Top = Owner.Top + 100; }
            Brightness.Focus();
        };
    }

    private void SettingsChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (initialized && !settingValues && !closed) PendingPreview = UpdatePreview();
    }
    private async Task UpdatePreview(bool immediate = false)
    {
        long version = ++request;
        pending?.Cancel(); var cancellation = new CancellationTokenSource(); pending = cancellation;
        result = null; previewError = null; ApplyButton.IsEnabled = false; PreviewStatus.Text = "Updating preview…";
        var settings = new ColorAdjustment(Brightness.Value, Contrast.Value, Saturation.Value);
        try
        {
            if (!immediate) await Task.Delay(180, cancellation.Token);
            var pixels = await Task.Run(() => ColorAdjustments.Apply(layer.Pixels, settings, cancellation.Token), cancellation.Token);
            if (closed || version != request) return;
            // A full-image edit can exceed the history cap. Never apply a change whose own Undo would be evicted.
            var next = original.Replace(layer with { Pixels = pixels });
            // Compare against the captured original, not the currently displayed preview.
            var retention = EditorSession.UndoBytesRequired(original, next);
            if (retention > EditorSession.MaxHistoryBytes)
                throw new InvalidOperationException("This change exceeds the 256 MiB Undo limit. Cancel and use a smaller source image.");
            result = pixels; PresentPreview(); ApplyButton.IsEnabled = true;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception error)
        {
            if (!closed && version == request) { session.Preview(original); previewError = error.Message; PreviewStatus.Text = previewError; }
        }
        finally
        {
            if (ReferenceEquals(pending, cancellation)) pending = null;
            cancellation.Dispose();
        }
    }
    private void PresentPreview()
    {
        session.Preview(ShowPreview.IsChecked == true && result is not null ? original.Replace(layer with { Pixels = result }) : original);
        PreviewStatus.Text = result is null ? previewError ?? "Updating preview…" : ShowPreview.IsChecked == true ? "Preview ready" : "Showing original; Apply uses the adjusted colors.";
    }
    private void TogglePreview(object sender, RoutedEventArgs e) => PresentPreview();
    private void Reset(object sender, RoutedEventArgs e) => SetSettings(default);
    internal void SetSettings(ColorAdjustment settings)
    {
        settings.Validate(); settingValues = true;
        try { Brightness.Value = settings.Brightness; Contrast.Value = settings.Contrast; Saturation.Value = settings.Saturation; }
        finally { settingValues = false; }
        PendingPreview = UpdatePreview(immediate: true);
    }
    internal void SetPreviewVisible(bool visible) { ShowPreview.IsChecked = visible; PresentPreview(); }
    private void Apply(object sender, RoutedEventArgs e) => ApplyEdit();
    internal void ApplyEdit()
    {
        if (closed || result is null || !ApplyButton.IsEnabled) return;
        session.Preview(original.Replace(layer with { Pixels = result })); session.Commit(); finished = true; Close();
    }
    internal void CancelEdit()
    {
        if (closed) return;
        closed = true; ++request; pending?.Cancel();
        if (!finished) session.Cancel();
    }
}
