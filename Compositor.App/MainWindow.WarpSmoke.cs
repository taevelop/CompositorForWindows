using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;
public partial class MainWindow
{
    internal async Task WarpToolSmoke(string report)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var doc = Document.Create(64, 64);var bytes = new byte[64 * 64 * 4];
        for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++) { int p = (y * 64 + x) * 4;bytes[p] = x < 32 ? (byte)255 : (byte)0;bytes[p + 3] = 255; }
        doc = doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(64, 64, bytes) });
        foreach (var mode in new[] { SmearMode.Smudge, SmearMode.Liquify })
        {
            session.Load(doc);ToolPicker.SelectedIndex = 15;SmearSmudge.IsChecked = mode == SmearMode.Smudge;SmearLiquify.IsChecked = mode == SmearMode.Liquify;
            BrushSize.Text = "4";BrushHardness.Text = "100";BrushOpacity.Text = "50";Canvas.Fit();
            Check(Canvas.ReadSmear() == mode && SmearOptions.IsVisible && !ColorOptions.IsVisible, "Smear mode controls failed.");
            Canvas.BeginPointer(new(31, 32), ModifierKeys.None);Canvas.MovePointer(new(35, 32));
            var clock = System.Diagnostics.Stopwatch.StartNew();
            while (ReferenceEquals(Canvas.DisplayDocument, doc) && clock.Elapsed < TimeSpan.FromSeconds(5)) await Task.Delay(10);
            Check(!ReferenceEquals(Canvas.DisplayDocument, doc) && ReferenceEquals(session.Document, doc) && session.InTransaction && !session.CanUndo, "Warp preview entered document or did not display.");
            Canvas.EndPointer(true);Check(await Canvas.WarpCompletion, "Warp completion failed.");
            var result = session.Document;Check(session.UndoCount == 1 && !session.InTransaction && !ReferenceEquals(result, doc), "Warp lost one commit.");
            session.Undo();Check(ReferenceEquals(doc, session.Document), "Warp Undo lost source.");session.Redo();Check(ReferenceEquals(result, session.Document), "Warp Redo lost result.");
            ProjectStore.Save(result, session.ActiveLayerId, report + "." + mode + ".comp");
            Check(CompositePixels(result).SequenceEqual(CompositePixels(ProjectStore.Load(report + "." + mode + ".comp").Document)), "Warp project pixels differ.");
            ImageCodec.Export(result, report + "." + mode + ".png", false);Check(CompositePixels(result).SequenceEqual(ImageCodec.Load(report + "." + mode + ".png").ToRgba()), "Warp PNG differs.");
            var tools = CaptureTabTools();SmearBlur.IsChecked = true;RestoreTabTools(tools);Check(Canvas.ReadSmear() == mode, "Tab tools lost smear mode.");
        }
        session.Load(doc);SmearSmudge.IsChecked = true;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Canvas.WarpPrepareOverride = (basis, id, settings, mode, token) => { entered.TrySetResult();release.Task.GetAwaiter().GetResult();return new(basis, id, settings, mode, token); };
        try
        {
            Canvas.BeginPointer(new(31, 32), ModifierKeys.None);Canvas.MovePointer(new(35, 32));await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var command = ResolveGradientBeforeAction();Check(!command.IsCompleted && busy && !Editor.IsEnabled, "Warp command did not wait.");
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(this), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            WindowKeyDown(this, escape);release.TrySetResult();Check(!await command && !await Canvas.WarpCompletion && ReferenceEquals(session.Document, doc) && !session.InTransaction && !busy && Editor.IsEnabled, "Warp busy Esc failed.");
            entered = new(TaskCreationOptions.RunContinuationsAsynchronously);release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.BeginPointer(new(31, 32), ModifierKeys.None);await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));session.Load(doc);session.Begin();long generation = session.TransactionGeneration;release.TrySetResult();
            Check(!await Canvas.WarpCompletion && session.InTransaction && generation == session.TransactionGeneration, "Late warp closed new generation.");session.Cancel();Canvas.CancelInteraction();
        }
        finally { release.TrySetResult();Canvas.WarpPrepareOverride = null;Canvas.CancelInteraction(); }
        File.WriteAllText(report, JsonSerializer.Serialize(new { passed = true, checks = new[] { "two modes", "display-only preview", "single Undo/Redo", "project/PNG pixels", "tab mode", "pending command", "busy Esc", "late generation" } }));
        foreach (var tab in workspace.Documents) tab.Session.MarkSaved();Close();
    }
}
