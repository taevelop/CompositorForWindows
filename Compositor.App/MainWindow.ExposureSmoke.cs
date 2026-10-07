using System.IO;
using System.Text.Json;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public partial class MainWindow
{
    private async Task ExposureSmokeTest(string reportPath)
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "exposure-smoke-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        void Dialog(bool create, Action<ExposureWindow> action)
        {
            var dialog = new ExposureWindow(session, create) { Owner = this }; Exception? failure = null;
            dialog.Loaded += (_, _) => { try { action(dialog); } catch (Exception e) { failure = e; } finally { if (dialog.IsVisible) dialog.Close(); } };
            try { dialog.ShowDialog(); } finally { dialog.CancelEdit(); Refresh(); }
            if (failure is not null) throw new InvalidOperationException("Exposure dialog failed.", failure);
        }
        try
        {
            var doc = Document.Create(800, 600); var fill = new BrushStroke(doc.Layers[0], new(2000, 1, .7, 70, 120, 180), 800, 600); fill.Append(new(400, 300));
            var source = doc.Layers[0] with { Pixels = fill.Pixels }; doc = doc.Replace(source); session.Load(doc); Canvas.Fit();
            Dialog(true, dialog => { dialog.SetSettings(new(1)); Check(session.ActiveLayer!.Exposure is not null, "New preview not selected."); dialog.Close(); });
            Check(ReferenceEquals(doc, session.Document) && session.ActiveLayerId == source.Id && !session.IsModified, "Cancel create lost document or selection.");
            Dialog(true, dialog => { dialog.SetSettings(new(1, -.02, 1.2)); dialog.SetPreviewVisible(false); Check(ReferenceEquals(doc, session.Document), "Original comparison failed."); dialog.ApplyEdit(); });
            var adjustment = session.ActiveLayer!; Check(adjustment.Exposure == new ExposureAdjustment(1, -.02, 1.2) && session.UndoCount == 1, "Create was not one Undo.");
            Check(ReferenceEquals(source.Pixels, session.Document.Layers[0].Pixels) && !AdjustColorsButton.IsEnabled && EditExposureMenu.IsEnabled, "Exposure source/UI guard failed.");
            Dialog(false, dialog => { dialog.SetSettings(new(3)); dialog.ExposureValue.Text = "NaN"; Check(!dialog.Preview(), "Invalid exposure accepted."); dialog.ApplyEdit(); Check(dialog.IsVisible, "Invalid Apply closed the window."); });
            Check(session.ActiveLayer!.Exposure == adjustment.Exposure, "Cancel edit lost saved settings.");
            Dialog(false, dialog =>
            {
                dialog.SetSettings(new(.75, 0, 1.1)); dialog.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.ChangeExtension(reportPath, ".dialog.png"))) encoder.Save(file);
                dialog.ApplyEdit();
            });
            Undo(this, new()); Check(session.ActiveLayer!.Exposure == adjustment.Exposure, "Undo exposure edit failed."); Redo(this, new());
            AddRevealMask(this, new()); ToolPicker.SelectedIndex = 1; MaskGray.Text = "0"; BrushSize.Text = "180"; BrushHardness.Text = "60";
            Canvas.BeginPointer(new(200, 200)); Canvas.MovePointer(new(600, 400)); Canvas.EndPointer(true);
            Check(session.ActiveLayer!.Mask!.Pixels.Width == 800 && session.ActiveLayer.Pixels.Tiles.Count == 0, "Exposure mask painting created source pixels.");
            var masked = session.Document; Canvas.BeginPointer(new(100, 100)); Canvas.CancelInteraction(); Check(ReferenceEquals(masked, session.Document), "Exposure mask cancel failed.");
            EditTarget.SelectedIndex = 0;
            bool rejected = false; try { Canvas.BeginPointer(new(100, 100)); } catch (InvalidOperationException) { rejected = true; } finally { Canvas.CancelInteraction(); }
            Check(rejected, "Exposure source painting was allowed.");
            projectPath = Path.Combine(root, "Exposure.comp"); Check(await Save(false), "Exposure UI save failed."); var expected = CompositePixels(session.Document);
            Check(await ReopenSavedProject(projectPath!), "Exposure reopen failed."); Check(expected.SequenceEqual(CompositePixels(session.Document)), "Exposure reopen changed output.");
            Check(session.ActiveLayer!.Exposure == new ExposureAdjustment(.75, 0, 1.1), "Reopen lost editable settings.");
            Dialog(false, dialog => { dialog.SetSettings(new(2)); dialog.ApplyEdit(); }); Undo(this, new());
            Check(expected.SequenceEqual(CompositePixels(session.Document)) && !session.IsModified, "Reopened edit Undo lost saved state.");
            string png = Path.Combine(root, "Exposure.png"); ImageCodec.Export(session.Document, png, false); Check(expected.SequenceEqual(ImageCodec.Load(png).ToRgba()), "Exposure PNG differs.");
            WrapInGroup(this, new()); var group = session.ActiveLayer!; Dialog(true, dialog => dialog.ApplyEdit());
            Check(session.ActiveLayer!.ParentId == group.Id, "Exposure creation inside a group failed."); Undo(this, new());
            session.ActiveLayerId = adjustment.Id; Refresh(); Canvas.Fit(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow,
                checks = new[] { "create/cancel preserves selection", "create/apply one Undo", "original comparison", "invalid values remain open", "edit/cancel/undo/redo", "source pixels preserved", "mask brush/cancel", "reject image painting", "UI save/reopen and re-edit", "PNG matches composite", "create inside group" },
                note = "Hidden WPF modal controls; no physical input or real Mac round trip." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Canvas.CancelInteraction(); projectPath = null; Refresh();
            string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetDirectoryName(reportPath)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("exposure-smoke-")) throw new IOException("Unsafe cleanup.");
            Directory.Delete(full, true);
        }
    }
}
