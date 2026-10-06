using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;
public partial class MainWindow
{
    private void StrokeSmokeTest(string reportPath)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Dialog(Action<StrokeWindow> action)
        {
            var dialog = new StrokeWindow(session) { Owner = this }; Exception? failure = null;
            dialog.Loaded += (_, _) => { try { action(dialog); } catch (Exception ex) { failure = ex; } finally { if (dialog.IsVisible) dialog.Close(); } };
            try { dialog.ShowDialog(); } finally { dialog.CancelEdit(); Refresh(); }
            if (failure is not null) throw new InvalidOperationException("Stroke UI regression.", failure);
        }
        var doc = Document.Create(800, 600);
        var stroke = new BrushStroke(doc.Layers[0], new(180, .8, 1, 80, 140, 200), 800, 600);
        stroke.Append(new(300, 260)); stroke.Append(new(480, 320));
        doc = doc.Replace(doc.Layers[0] with { Pixels = stroke.Pixels, Effects = new(new(.1, .3, .8, .25)) });
        session.Load(doc); Canvas.Fit();
        var effect = new StrokeEffect(8, .1, .2, .3, .7);
        Dialog(d => { d.SetSettings(effect); Check(session.ActiveLayer!.Effects!.Stroke == effect, "No stroke preview."); });
        Check(ReferenceEquals(doc, session.Document), "Stroke cancel changed original.");
        Dialog(d => { d.SetSettings(effect); d.PreviewEnabled.IsChecked = false; d.Preview(); Check(ReferenceEquals(doc, session.Document), "Stroke compare changed original."); d.ApplyEdit(); });
        Check(session.UndoCount == 1 && session.ActiveLayer!.Effects!.Stroke == effect, "Stroke apply failed.");
        Check(session.ActiveLayer!.Effects!.ColorOverlay == doc.Layers[0].Effects!.ColorOverlay, "Stroke lost overlay.");
        session.Undo(); Check(session.ActiveLayer!.Effects!.Stroke is null, "Stroke undo failed."); session.Redo();
        int undo = session.UndoCount;
        Dialog(d => d.ApplyEdit()); Check(session.UndoCount == undo, "Stroke no-op created history.");
        Dialog(d => { d.SizeInput.Text = "invalid"; Check(!d.Preview(), "Invalid stroke input accepted."); });
        Dialog(d =>
        {
            d.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)d.ActualWidth, (int)d.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(d);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.ChangeExtension(reportPath, ".dialog.png")); png.Save(stream);
            d.RemoveEdit();
        });
        Check(session.ActiveLayer!.Effects!.Stroke is null && session.ActiveLayer.Effects.ColorOverlay is not null, "Remove lost other effects.");
        session.Undo();
        var overlay = new ColorOverlayWindow(session) { Owner = this };
        overlay.Loaded += (_, _) => overlay.RemoveEdit();
        try { overlay.ShowDialog(); } finally { overlay.CancelEdit(); }
        Check(session.ActiveLayer!.Effects!.Stroke == effect, "Overlay removal lost stroke."); session.Undo();
        Refresh();
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed = true, checks = new[] {
            "live preview", "cancel and comparison", "apply/undo/redo/no-op", "invalid input",
            "remove preserves overlay", "overlay removal preserves stroke", "dialog render" } }));
    }
}
