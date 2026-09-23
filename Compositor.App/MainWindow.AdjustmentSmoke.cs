using System.IO;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public partial class MainWindow
{
    private async Task AdjustmentSmokeTest(string reportPath)
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "adjustment-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        void Dialog(Func<ColorAdjustmentWindow, Task> scenario)
        {
            var dialog = new ColorAdjustmentWindow(session) { Owner = this }; Exception? failure = null;
            dialog.Loaded += async (_, _) =>
            {
                try { await scenario(dialog); }
                catch (Exception error) { failure = error; }
                finally { if (dialog.IsVisible) dialog.Close(); }
            };
            try { dialog.ShowDialog(); }
            finally { dialog.CancelEdit(); }
            if (failure is not null) throw new InvalidOperationException("Adjustment dialog smoke failed.", failure);
        }
        try
        {
            const int width = 800, height = 600;
            var bytes = new byte[width * height * 4];
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++)
            {
                int p = (y * width + x) * 4; byte alpha = (byte)(64 + 191 * x / (width - 1));
                bytes[p] = (byte)(alpha * x / width); bytes[p + 1] = (byte)(alpha * y / height); bytes[p + 2] = (byte)(alpha / 2); bytes[p + 3] = alpha;
            }
            var doc = Document.Create(width, height); var group = Layer.Group("Color study", width, height);
            var layer = doc.Layers[0] with { Name = "Adjusted image", Pixels = Raster.FromRgba(width, height, bytes), ParentId = group.Id, Mask = LayerMask.Solid(1, 1, 220) };
            doc = doc with { Layers = [group, layer] }; session.Load(doc, layer.Id); Canvas.Fit();
            Check(AdjustColorsButton.IsEnabled, "Image color control is disabled.");
            session.ActiveLayerId = group.Id; Refresh(); Check(!AdjustColorsButton.IsEnabled, "Group color editing was enabled.");
            session.ActiveLayerId = layer.Id; session.EditMask = true; Refresh(); Check(!AdjustColorsMenu.IsEnabled, "Mask color editing was enabled.");
            session.EditMask = false; Refresh();
            session.Apply(d => d.Replace(layer with { Name = "Temporary" })); session.Undo();
            var settings = new ColorAdjustment(12, 18, 40); var expected = ColorAdjustments.Apply(layer.Pixels, settings);
            Dialog(async dialog =>
            {
                dialog.SetSettings(new(80, -30, -100)); var obsolete = dialog.PendingPreview;
                dialog.SetSettings(settings); await dialog.PendingPreview; await obsolete;
                Check(session.ActiveLayer!.Pixels.ToRgba().SequenceEqual(expected.ToRgba()), "Preview accumulated settings or published a stale result.");
                Check(ReferenceEquals(layer.Mask, session.ActiveLayer.Mask), "Color preview changed the mask.");
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                dialog.UpdateLayout();
                var screenshot = new System.Windows.Media.Imaging.RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, System.Windows.Media.PixelFormats.Pbgra32);
                screenshot.Render(dialog);
                var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder(); encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(screenshot));
                using (var file = File.Create(Path.ChangeExtension(reportPath, ".dialog.png"))) encoder.Save(file);
                dialog.SetPreviewVisible(false); Check(ReferenceEquals(doc, session.Document), "Original comparison did not restore the starting document.");
                dialog.SetPreviewVisible(true); Check(!ReferenceEquals(layer.Pixels, session.ActiveLayer!.Pixels), "Preview comparison did not restore adjusted pixels.");
                dialog.Close();
            });
            Check(ReferenceEquals(doc, session.Document) && session.CanRedo && !session.IsModified && !session.InTransaction, "Cancel lost the original, redo or clean state.");
            Dialog(async dialog =>
            {
                dialog.SetSettings(settings); await dialog.PendingPreview;
                dialog.SetSettings(default); await dialog.PendingPreview; dialog.ApplyEdit();
            });
            Check(ReferenceEquals(doc, session.Document) && session.CanRedo && session.UndoCount == 0 && !session.IsModified, "Reset/Apply created an edit.");
            Task? canceled = null;
            Dialog(dialog =>
            {
                dialog.SetSettings(settings); canceled = dialog.PendingPreview; dialog.Close(); return Task.CompletedTask;
            });
            await canceled!;
            Check(ReferenceEquals(doc, session.Document) && !session.InTransaction, "Closed dialog published late work.");
            Dialog(async dialog =>
            {
                dialog.SetSettings(settings); await dialog.PendingPreview; dialog.SetPreviewVisible(false); dialog.ApplyEdit();
            });
            Check(session.ActiveLayer!.Pixels.ToRgba().SequenceEqual(expected.ToRgba()) && session.UndoCount == 1 && !session.CanRedo, "Apply did not commit exactly once, or applied original comparison.");
            var applied = session.Document; Undo(this, new()); Check(ReferenceEquals(doc, session.Document), "Adjustment Undo lost original pixels.");
            Redo(this, new()); Check(ReferenceEquals(applied, session.Document), "Adjustment Redo failed.");
            projectPath = Path.Combine(root, "Adjusted.comp"); Check(await Save(false), "Adjusted UI save failed.");
            var composite = CompositePixels(session.Document); Check(await LoadProject(projectPath, false), "Adjusted UI load failed.");
            Check(composite.SequenceEqual(CompositePixels(session.Document)) && !session.IsModified, "Adjustment save round trip differs.");
            string png = Path.Combine(root, "Adjusted.png"); ImageCodec.Export(session.Document, png, false);
            Check(composite.SequenceEqual(ImageCodec.Load(png).ToRgba()), "Adjustment output differs from the composite.");
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow,
                checks = new[] { "image-only controls", "latest request wins", "preview from immutable original", "mask preserved", "original comparison", "cancel restores clean and redo", "reset no-op", "close cancels pending work", "apply with preview off", "single Undo/Redo", "UI save/reopen", "PNG matches composite" },
                note = "Hidden WPF modal dialogs and handlers; physical slider dragging and monitor presentation are not measured." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            projectPath = null; Refresh();
            string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetDirectoryName(reportPath)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("adjustment-smoke-")) throw new IOException("Unsafe adjustment smoke cleanup.");
            Directory.Delete(full, true);
        }
    }
}
