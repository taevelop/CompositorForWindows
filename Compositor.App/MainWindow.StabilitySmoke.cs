using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

namespace Compositor.App;

public partial class MainWindow
{
    internal async Task StabilitySmokeTest(string reportPath)
    {
        var display = session.Document;
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "smoke-projects-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var checks = new List<string>(); var errors = new List<string>();
        void Check(bool condition, string message) { if (!condition) throw new InvalidOperationException(message); }
        void Escape()
        {
            var e = new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(this)!, Environment.TickCount, Key.Escape)
                { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            RaiseEvent(e); Check(e.Handled, "Escape did not cancel the interaction.");
        }
        prompts = new(() => MessageBoxResult.Cancel, message => throw new InvalidOperationException("Unexpected dialog: " + message));
        try
        {
            session.Load(Document.Create(800, 600)); Canvas.ActualPixels(); ToolPicker.SelectedIndex = 0;
            var before = session.Document;
            Check(Canvas.BeginInteraction(MouseButton.Left, new(180, 160)), "Move did not start.");
            Check(!Canvas.BeginInteraction(MouseButton.Middle, new(180, 160)), "Middle button interrupted a left drag.");
            Canvas.MoveInteraction(new(220, 180));
            double zoom = Canvas.Zoom; Canvas.ZoomAt(new(200, 200), 120); Check(Canvas.Zoom == zoom, "Zoom changed during editing.");
            Check(!Canvas.FinishInteraction(MouseButton.Right, new(220, 180)) && session.InTransaction, "Right mouse-up committed a left drag.");
            OnDeactivated(EventArgs.Empty);
            Check(!Canvas.HasInteraction && ReferenceEquals(before, session.Document) && !session.IsModified, "Deactivation did not roll back the drag.");
            checks.Add("Mixed mouse buttons, wheel during edit, window deactivation");

            Canvas.BeginInteraction(MouseButton.Left, new(180, 160)); Canvas.MoveInteraction(new(220, 180)); Canvas.MoveInteraction(new(180, 160));
            Check(Canvas.FinishInteraction(MouseButton.Left, new(180, 160)), "Left mouse-up did not complete a move.");
            Check(!session.IsModified && !session.CanUndo, "Returning to the original position created history.");
            checks.Add("No-op move retains clean state");

            ToolPicker.SelectedIndex = 1;
            Canvas.BeginInteraction(MouseButton.Left, new(180, 160)); Canvas.MoveInteraction(new(240, 180)); Escape();
            Check(!session.InTransaction && ReferenceEquals(before, session.Document), "Escape did not restore the document.");
            Canvas.BeginInteraction(MouseButton.Left, new(180, 160));
            Canvas.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, Environment.TickCount) { RoutedEvent = Mouse.LostMouseCaptureEvent });
            Check(!Canvas.HasInteraction && ReferenceEquals(before, session.Document), "Capture loss did not roll back the stroke.");
            checks.Add("Escape and routed capture-loss cancel strokes");

            var pointBeforePan = Canvas.DocumentPoint(new(200, 200));
            Canvas.BeginInteraction(MouseButton.Middle, new(100, 100)); Canvas.MoveInteraction(new(170, 160));
            Check(Canvas.DocumentPoint(new(200, 200)) != pointBeforePan, "Pan did not change the viewport.");
            Escape(); Check(Canvas.DocumentPoint(new(200, 200)) == pointBeforePan && !Canvas.HasInteraction, "Escape did not restore pan.");
            checks.Add("Pan cancellation restores the viewport");

            string png = Path.Combine(root, "input.png"), jpeg = Path.Combine(root, "input.jpg");
            var seed = Document.Create(96, 64); var brush = new BrushStroke(seed.Layers[0], new(50, .5, .7, 200, 50, 30), 96, 64);
            brush.Append(new(30, 30)); brush.Append(new(70, 40)); seed = seed.Replace(seed.Layers[0] with { Pixels = brush.Pixels });
            ImageCodec.Export(seed, png, false); ImageCodec.Export(seed, jpeg, true);
            await Import([png, jpeg]); Check(session.Document.Layers.Length == 3, "PNG/JPEG import failed.");
            LayerOpacity.Text = "65"; LayerRotation.Text = "17"; ApplyLayer(this, new());
            var importedLayer = session.ActiveLayer!;
            var middle = importedLayer.Transform.ToDocument(new(48, 32), 96, 64);
            Canvas.BeginPointer(middle); Canvas.MovePointer(new(middle.X + 10, middle.Y)); Canvas.EndPointer(true);
            ToolPicker.SelectedIndex = 2;
            Canvas.BeginPointer(middle); Canvas.MovePointer(new(middle.X - 10, middle.Y)); Canvas.EndPointer(true);
            var edited = session.Document; session.Undo(); session.Redo(); Check(ReferenceEquals(edited, session.Document), "Redo lost the edited snapshot.");
            projectPath = Path.Combine(root, "Workflow.comp"); Check(await Save(false), "Workflow save failed.");
            byte[] expected = CompositePixels(session.Document);
            Check(await LoadProject(projectPath, false), "Workflow reopen failed.");
            Check(!session.IsModified && !session.CanUndo && expected.SequenceEqual(CompositePixels(session.Document)), "Reopen changed pixels or retained history.");
            string export = Path.Combine(root, "output.png"); ImageCodec.Export(session.Document, export, false);
            Check(expected.SequenceEqual(ImageCodec.Load(export).ToRgba()), "Export differs from the current composite.");
            ImageCodec.Export(session.Document, Path.Combine(root, "output.jpg"), true);
            checks.Add("PNG/JPEG import, layer properties, brush/eraser, undo/redo, save/reopen, export");

            string invalid = Path.Combine(root, "Unsupported.comp"); ProjectStore.Save(session.Document, session.ActiveLayerId, invalid);
            string manifest = Path.Combine(invalid, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(manifest))!;
            json["layers"]![0]!["effects"] = new JsonObject(); File.WriteAllText(manifest, json.ToJsonString());
            session.Apply(d => d.Replace(d.Layers[0] with { Name = "Keep pending edits" }));
            int undoBeforeLoad = session.UndoCount;
            before = session.Document; prompts = new(() => MessageBoxResult.Cancel, errors.Add);
            Check(!await LoadProject(invalid, false) && ReferenceEquals(before, session.Document) && errors.Count == 1, "Unsupported load changed the current document.");
            Check(session.UndoCount == undoBeforeLoad && session.IsModified, "Unsupported load changed history or dirty state.");
            File.WriteAllText(manifest, "{invalid-json");
            Check(!await LoadProject(invalid, false) && ReferenceEquals(before, session.Document) && errors.Count == 2 && session.UndoCount == undoBeforeLoad && session.IsModified, "Corrupt load changed the current document or history.");
            checks.Add("Unsupported and corrupt projects keep the current document, history, and dirty state");

            string source = projectPath!; Directory.Move(source, source + ".recovery");
            byte[] recoveryMetadata = File.ReadAllBytes(Path.Combine(source + ".recovery", "manifest.json"));
            Check(await LoadProject(source + ".recovery", true) && session.IsModified && projectPath is null, "Recovery did not open as an unsaved copy.");
            projectPath = Path.Combine(root, "Recovered.comp"); Check(await Save(false), "Saving the recovered copy failed.");
            Check(recoveryMetadata.SequenceEqual(File.ReadAllBytes(Path.Combine(source + ".recovery", "manifest.json"))), "Recovery source was modified.");
            checks.Add("Recovery opens as a separate unsaved document and preserves its source");

            session.Apply(d => d.Replace(d.Layers[0] with { Name = "Unsaved edit" }));
            prompts = new(() => MessageBoxResult.Cancel, errors.Add); Close();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            Check(IsVisible && session.IsModified, "Cancel closed the dirty window.");
            prompts = new(() => MessageBoxResult.No, errors.Add); Check(await ConfirmDiscard() && session.IsModified, "Discard confirmation changed the document.");
            prompts = new(() => MessageBoxResult.Yes, errors.Add);
            string savedManifest = Path.Combine(projectPath!, "manifest.json"); byte[] savedBytes = File.ReadAllBytes(savedManifest);
            using (var writeLock = new FileStream(projectPath + ".write-lock", FileMode.Open, FileAccess.ReadWrite, FileShare.None))
                Check(!await ConfirmDiscard() && IsVisible && session.IsModified, "A failed save allowed close or lost edits.");
            Check(savedBytes.SequenceEqual(File.ReadAllBytes(savedManifest)), "Failed save changed the original package.");
            Check(await ConfirmDiscard() && !session.IsModified, "Save-before-close did not persist edits.");
            checks.Add("Unsaved close: cancel, discard choice, failed save, successful save");

            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow, checks,
                note = "Hidden WPF routes and controlled dialog responses. Does not simulate physical mouse hardware, a real process crash, or monitor DPI changes." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Canvas.CancelInteraction(); prompts = defaultPrompts; projectPath = null;
            session.Load(display); ToolPicker.SelectedIndex = 0; Canvas.Fit();
            // These are generated fixtures only; the report and screenshot remain in artifacts.
            string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetDirectoryName(reportPath)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("smoke-projects-")) throw new IOException("Unsafe smoke cleanup path.");
            Directory.Delete(full, true);
        }
    }

    private static byte[] CompositePixels(Document document)
    {
        using var renderer = new CanvasRenderer(); using var image = renderer.Flatten(document);
        using var bitmap = new SKBitmap(CanvasRenderer.Info(document.Width, document.Height));
        if (!image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)) throw new IOException("Cannot read composite.");
        return bitmap.GetPixelSpan().ToArray();
    }
}
