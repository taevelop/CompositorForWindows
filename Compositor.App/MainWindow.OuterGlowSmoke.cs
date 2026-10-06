using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;
public partial class MainWindow
{
    private void OuterGlowSmokeTest(string reportPath)
    {
        void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        void Dialog(Action<OuterGlowWindow> action)
        {
            var dialog = new OuterGlowWindow(session) { Owner = this }; Exception? failure = null;
            dialog.Loaded += (_, _) => { try { action(dialog); } catch (Exception ex) { failure = ex; } finally { if (dialog.IsVisible) dialog.Close(); } };
            try { dialog.ShowDialog(); } finally { dialog.CancelEdit(); Refresh(); }
            if (failure is not null) throw new InvalidOperationException("OuterGlow UI regression.", failure);
        }
        var doc = Document.Create(800, 600);
        var glow = new BrushStroke(doc.Layers[0], new(180, .8, 1, 80, 140, 200), 800, 600);
        glow.Append(new(300, 260)); glow.Append(new(480, 320));
        doc = doc.Replace(doc.Layers[0] with { Pixels = glow.Pixels, Effects = new(new(.1, .3, .8, .25)) });
        session.Load(doc); Canvas.Fit();
        var effect = new OuterGlowEffect(8, .1, .2, .3, .7);
        Dialog(d => { d.SetSettings(effect); Check(session.ActiveLayer!.Effects!.OuterGlow == effect, "No glow preview."); });
        Check(ReferenceEquals(doc, session.Document), "OuterGlow cancel changed original.");
        Dialog(d => { d.SetSettings(effect); d.PreviewEnabled.IsChecked = false; d.Preview(); Check(ReferenceEquals(doc, session.Document), "OuterGlow compare changed original."); d.ApplyEdit(); });
        Check(session.UndoCount == 1 && session.ActiveLayer!.Effects!.OuterGlow == effect, "OuterGlow apply failed.");
        Check(session.ActiveLayer!.Effects!.ColorOverlay == doc.Layers[0].Effects!.ColorOverlay, "OuterGlow lost overlay.");
        session.Undo(); Check(session.ActiveLayer!.Effects!.OuterGlow is null, "OuterGlow undo failed."); session.Redo();
        int undo = session.UndoCount;
        Dialog(d => d.ApplyEdit()); Check(session.UndoCount == undo, "OuterGlow no-op created history.");
        Dialog(d => { d.SizeInput.Text = "invalid"; Check(!d.Preview(), "Invalid glow input accepted."); });
        Dialog(d =>
        {
            d.UpdateLayout();
            var bitmap = new RenderTargetBitmap((int)d.ActualWidth, (int)d.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(d);
            var png = new PngBitmapEncoder(); png.Frames.Add(BitmapFrame.Create(bitmap));
            using var stream = File.Create(Path.ChangeExtension(reportPath, ".dialog.png")); png.Save(stream);
            d.RemoveEdit();
        });
        Check(session.ActiveLayer!.Effects!.OuterGlow is null && session.ActiveLayer.Effects.ColorOverlay is not null, "Remove lost other effects.");
        session.Undo();
        var overlay = new ColorOverlayWindow(session) { Owner = this };
        overlay.Loaded += (_, _) => overlay.RemoveEdit();
        try { overlay.ShowDialog(); } finally { overlay.CancelEdit(); }
        Check(session.ActiveLayer!.Effects!.OuterGlow == effect, "Overlay removal lost glow."); session.Undo();
        Refresh();
        File.WriteAllText(reportPath, JsonSerializer.Serialize(new { passed = true, checks = new[] {
            "live preview", "cancel and comparison", "apply/undo/redo/no-op", "invalid input",
            "remove preserves overlay", "overlay removal preserves glow", "dialog render" } }));
    }
}
