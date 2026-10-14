using Compositor.Core;
using Compositor.Imaging;
using System.Windows.Input;

namespace Compositor.App;
public partial class MainWindow
{
    private async Task CloneSmoke(string? reportPath = null)
    {
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
            var source = Canvas.CloneSource; AddDocumentTab(Document.Create(8, 4)); var second = workspace.Current;
            Check(Canvas.CloneSource.Source is null, "New tab inherited clone source.");
            await SelectTabAsync(first.Id); Check(ReferenceEquals(source, Canvas.CloneSource) && ToolPicker.SelectedIndex == 13, "Tab lost clone state.");
            Check(workspace.Close(second.Id, true), "Clone fixture did not close."); tabViews.Remove(second.Id);
        }
        finally
        {
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
        System.IO.File.WriteAllText(reportPath, System.Text.Json.JsonSerializer.Serialize(new { passed = true, checks = new[] { "Alt-source without Undo", "source-over clone click", "source indicator offset", "Undo/Redo", "cancel", "project roundtrip", "PNG/composite match", "independent tab sources" }, note = "Injected WPF pointer path; physical input, tip preview and raster expansion remain separate." }));
        foreach (var tab in workspace.Documents) tab.Session.MarkSaved(); Close();
    }
}
