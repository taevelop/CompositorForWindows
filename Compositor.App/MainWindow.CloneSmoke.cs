using Compositor.Core;
using Compositor.Imaging;
using System.Windows.Input;

namespace Compositor.App;
public partial class MainWindow
{
    private async Task CloneSmoke(string? reportPath = null)
    {
        await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var saved = session.Document; var active = session.ActiveLayerId; var tools = CaptureTabTools();
        var first = workspace.Current;
        var directory = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Compositor-clone-" + Guid.NewGuid().ToString("N"));
        try
        {
            var doc = Document.Create(8, 4);var bytes = new byte[8 * 4 * 4];bytes[0] = 100;bytes[3] = 255;
            doc = doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(8, 4, bytes) }); session.Load(doc);
            Canvas.CloneSource = new(); ToolPicker.SelectedIndex = 13; BrushSize.Text = "1"; BrushHardness.Text = "100"; BrushOpacity.Text = "100";
            Check(Canvas.Tool == EditorTool.CloneStamp && CloneOptions.IsVisible && !ColorOptions.IsVisible, "Clone controls missing.");
            Canvas.BeginPointer(new(.5, .5), ModifierKeys.Alt); Check(!session.InTransaction && !session.CanUndo, "Setting source edited document.");
            Canvas.BeginPointer(new(4.5, .5), ModifierKeys.None); Canvas.EndPointer(true);
            Check(session.UndoCount == 1 && session.ActiveLayer!.Pixels.ToRgba()[16] == 100, "Clone click did not copy source.");
            Check(Canvas.CloneIndicatorSource == new PointD(.5, .5), "Clone source indicator lost its aligned offset.");
            session.Undo(); Check(ReferenceEquals(doc, session.Document), "Clone Undo did not restore source."); session.Redo();
            var before = session.Document; Canvas.BeginPointer(new(5.5, .5), ModifierKeys.None); Canvas.CancelInteraction();
            Check(ReferenceEquals(before, session.Document), "Cancelled clone changed document.");
            System.IO.Directory.CreateDirectory(directory);
            var project = System.IO.Path.Combine(directory, "Clone.comp");
            ProjectStore.Save(before, session.ActiveLayerId, project); var loaded = ProjectStore.Load(project);
            Check(CompositePixels(before).SequenceEqual(CompositePixels(loaded.Document)), "Clone save/reopen changed output.");
            var png = System.IO.Path.Combine(directory, "Clone.png"); ImageCodec.Export(before, png, false);
            Check(CompositePixels(before).SequenceEqual(ImageCodec.Load(png).ToRgba()), "Clone PNG differs from canvas composite.");
            if (reportPath is not null)
            {
                Canvas.MovePointer(new(4.5, .5)); Canvas.Fit(); UpdateLayout();
                var bitmap = new System.Windows.Media.Imaging.RenderTargetBitmap((int)Math.Ceiling(ActualWidth), (int)Math.Ceiling(ActualHeight), 96, 96, System.Windows.Media.PixelFormats.Pbgra32); bitmap.Render(this);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));
                using var output = System.IO.File.Create(reportPath + ".png"); encoder.Save(output);
            }
            var small = Document.Create(8, 4); var smallBytes = new byte[16]; smallBytes[0] = 100; smallBytes[3] = 255;
            small = small.Replace(small.Layers[0] with { Pixels = Raster.FromRgba(2, 2, smallBytes), Transform = new(0, 0, 2, 2), Mask = LayerMask.Solid(2, 2) }); session.Load(small);
            BrushSize.Text = "4";BrushHardness.Text = "0";
            Canvas.BeginPointer(new(.5, .5), ModifierKeys.Alt);Canvas.BeginPointer(new(4.5, .5), ModifierKeys.None);
            Check(session.ActiveLayer!.Pixels.Width >= 7, "Clone draft did not include brush bounds.");Canvas.EndPointer(true);
            Check(session.ActiveLayer!.Pixels.Width == 5 && session.ActiveLayer.Pixels.ToRgba()[16] == 100 && session.ActiveLayer.Mask!.Pixels.Width == 5,
                "Clone canvas stroke did not expand pixels and linked mask.");
            session.Undo();Check(ReferenceEquals(small, session.Document), "Expanded clone Undo lost source dimensions.");session.Redo();
            ProjectStore.Save(session.Document, session.ActiveLayerId, project);Check(CompositePixels(session.Document).SequenceEqual(CompositePixels(ProjectStore.Load(project).Document)), "Expanded clone save/reopen changed output.");
            var source = Canvas.CloneSource; AddDocumentTab(Document.Create(8, 4)); var second = workspace.Current;
            Check(Canvas.CloneSource.Source is null, "New tab inherited clone source.");
            await SelectTabAsync(first.Id); Check(ReferenceEquals(source, Canvas.CloneSource) && ToolPicker.SelectedIndex == 13, "Tab lost clone state.");
            Check(workspace.Close(second.Id, true), "Clone fixture did not close."); tabViews.Remove(second.Id);
            session.Load(small);BrushSize.Text = "1";BrushHardness.Text = "100";Canvas.CloneSource.Set(new(.5, .5));
            var entered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.CloneCaptureOverride = (document, id, settings, token) => { entered.TrySetResult(true);release.Task.GetAwaiter().GetResult();return CloneStamp.Capture(document, id, settings, token); };
            Canvas.BeginPointer(new(4.5, .5), ModifierKeys.None);await entered.Task;
            Canvas.MovePointer(new(5.5, .5));Canvas.EndPointer(true);
            Check(Canvas.IsClonePreparing && session.InTransaction && session.UndoCount == 0 && ReferenceEquals(session.Document, small), "Preparing clone published an early edit.");
            release.TrySetResult(true);Check(await Canvas.CloneCompletion, "Queued clone did not finish.");
            Check(session.UndoCount == 1 && !session.InTransaction && session.ActiveLayer!.Pixels.ToRgba()[16] == 100, "Queued clone lost initial point or single Undo.");
            session.Load(small);entered = new(TaskCreationOptions.RunContinuationsAsynchronously);release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.BeginPointer(new(4.5, .5), ModifierKeys.None);await entered.Task;
            var escape = new KeyEventArgs(Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(this), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            WindowKeyDown(this, escape);Check(escape.Handled, "Clone preparation Esc was not handled.");release.TrySetResult(true);
            Check(!await Canvas.CloneCompletion && ReferenceEquals(session.Document, small) && !session.InTransaction && !session.CanUndo, "Cancelled preparation published a late edit.");
            session.Load(small);entered = new(TaskCreationOptions.RunContinuationsAsynchronously);release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Canvas.BeginPointer(new(4.5, .5), ModifierKeys.None);await entered.Task;session.Load(small);session.Begin();long generation = session.TransactionGeneration;release.TrySetResult(true);
            Check(!await Canvas.CloneCompletion && session.InTransaction && session.TransactionGeneration == generation, "Stale clone closed a newer transaction.");session.Cancel();
            Canvas.CloneCaptureOverride = null;
        }
        finally
        {
            Canvas.CloneCaptureOverride = null;Canvas.CancelInteraction();
            SelectTab(first.Id); session.Load(saved, active); RestoreTabTools(tools); Refresh();
            var root = System.IO.Path.GetFullPath(directory);
            var parent = System.IO.Path.GetFullPath(System.IO.Path.GetTempPath()).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            if (!root.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !System.IO.Path.GetFileName(root).StartsWith("Compositor-clone-")) throw new System.IO.IOException("Unsafe clone fixture cleanup.");
            if (System.IO.Directory.Exists(root)) System.IO.Directory.Delete(root, true);
        }
    }

    internal async Task CloneToolSmoke(string reportPath)
    {
        await CloneSmoke(reportPath);
        System.IO.File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new { passed = true, checks = new[] { "Alt-source without Undo", "source-over clone click", "source indicator offset", "Undo/Redo", "cancel", "project roundtrip", "PNG/composite match", "independent tab sources", "expansion and linked mask", "worker queued points and single Undo", "Esc discards late preparation", "new transaction ownership" }, note = "Injected WPF pointer/key and gated capture path; physical input and real large-document responsiveness remain separate." }));
        foreach (var tab in workspace.Documents) tab.Session.MarkSaved(); Close();
    }
}
