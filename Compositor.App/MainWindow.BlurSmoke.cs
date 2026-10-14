using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;
public partial class MainWindow
{
    internal async Task BlurToolSmoke(string report)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var doc = Document.Create(64, 64);var bytes = new byte[64 * 64 * 4];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
        { int p = (y * 64 + x) * 4;bytes[p] = bytes[p + 1] = bytes[p + 2] = x >= 32 ? (byte)255 : (byte)0;bytes[p + 3] = 255; }
        doc = doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(64, 64, bytes) });
        session.Load(doc);ToolPicker.SelectedIndex = 15;BrushSize.Text = "40";BrushHardness.Text = "100";BrushOpacity.Text = "50";Canvas.Fit();
        Check(Canvas.Tool == EditorTool.Blur && BrushOptions.IsVisible && !ColorOptions.IsVisible && !CloneOptions.IsVisible && ToolTitle.Text == "Blur", "Blur controls were not connected.");
        Canvas.BeginPointer(new(31.5, 32.5), ModifierKeys.None);Canvas.MovePointer(new(33.5, 32.5));Canvas.EndPointer(true);
        Check(await Canvas.CloneCompletion, "Blur preparation failed.");
        var result = session.Document;Check(session.UndoCount == 1 && !session.InTransaction && result.Layers[0].Pixels.ToRgba()[(32 * 64 + 31) * 4] > 0, "Blur did not soften or commit once.");
        session.Undo();Check(ReferenceEquals(doc, session.Document), "Blur Undo lost source.");session.Redo();Check(ReferenceEquals(result, session.Document), "Blur Redo lost result.");
        var path = report + ".comp";ProjectStore.Save(result, session.ActiveLayerId, path);
        Check(CompositePixels(result).SequenceEqual(CompositePixels(ProjectStore.Load(path).Document)), "Blur save round trip changed output.");
        ImageCodec.Export(result, report + ".png", false);Check(CompositePixels(result).SequenceEqual(ImageCodec.Load(report + ".png").ToRgba()), "Blur export differs from composite.");
        session.Load(doc);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Canvas.BlurCaptureOverride = (basis, id, diameter, token) => { entered.TrySetResult();release.Task.GetAwaiter().GetResult();return BlurBrush.Capture(basis, id, diameter, token); };
        try
        {
            Canvas.BeginPointer(new(31.5, 32.5), ModifierKeys.None);Canvas.EndPointer(true);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var pendingAction = ResolveGradientBeforeAction();
            Check(!pendingAction.IsCompleted && busy && !Editor.IsEnabled, "Command did not wait for Blur preparation.");
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(this), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            WindowKeyDown(this, escape);release.TrySetResult();Check(!await Canvas.CloneCompletion && ReferenceEquals(session.Document, doc) && !session.InTransaction, "Blur Esc published a late result.");
            Check(!await pendingAction && !busy && Editor.IsEnabled, "Cancelled Blur command did not restore UI.");
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.BeginPointer(new(31.5, 32.5), ModifierKeys.None);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            session.Load(doc);session.Begin();long generation = session.TransactionGeneration;release.TrySetResult();
            Check(!await Canvas.CloneCompletion && session.InTransaction && generation == session.TransactionGeneration, "Late blur closed a new transaction.");session.Cancel();Canvas.CancelInteraction();
        }
        finally { release.TrySetResult();Canvas.BlurCaptureOverride = null;Canvas.CancelInteraction(); }
        File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, checks = new[] { "Blur controls", "queued stroke", "single Undo/Redo", "project/output pixels", "Esc while capturing", "late generation protection" } }));
        foreach (var tab in workspace.Documents) tab.Session.MarkSaved();Close();
    }
}
