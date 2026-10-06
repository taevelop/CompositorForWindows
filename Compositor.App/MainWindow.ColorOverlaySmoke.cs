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
    private async Task ColorOverlaySmokeTest(string reportPath)
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "overlay-smoke-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        void Dialog(Action<ColorOverlayWindow> action)
        {
            var dialog = new ColorOverlayWindow(session) { Owner = this }; Exception? failure = null;
            dialog.Loaded += (_, _) => { try { action(dialog); } catch (Exception error) { failure = error; } finally { if (dialog.IsVisible) dialog.Close(); } };
            try { dialog.ShowDialog(); } finally { dialog.CancelEdit(); Refresh(); }
            if (failure is not null) throw new InvalidOperationException("Color overlay dialog failed.", failure);
        }
        try
        {
            var doc = Document.Create(800, 600); var fill = new BrushStroke(doc.Layers[0], new(2000, 1, .6, 70, 120, 180), 800, 600); fill.Append(new(400, 300));
            var source = doc.Layers[0] with { Pixels = fill.Pixels }; doc = doc.Replace(source); session.Load(doc); Canvas.Fit();
            var first = new ColorOverlayEffect(.9, .2, .4, .6); var second = new ColorOverlayEffect(.1, .75, .8, .4);
            Dialog(dialog => { dialog.SetSettings(first); Check(session.ActiveLayer!.Effects?.ColorOverlay == first, "No overlay preview."); dialog.Close(); });
            Check(ReferenceEquals(doc, session.Document) && !session.IsModified && session.ActiveLayerId == source.Id, "Cancel changed original/selection.");
            Dialog(dialog => { dialog.SetSettings(first); dialog.SetPreviewVisible(false); Check(ReferenceEquals(doc, session.Document), "Original comparison failed."); dialog.ApplyEdit(); });
            Check(session.UndoCount == 1 && session.Document.Layers.Length == 1 && session.ActiveLayer!.Effects!.ColorOverlay == first, "Apply was not one edit on the existing layer.");
            Check(ReferenceEquals(source.Pixels, session.ActiveLayer!.Pixels), "Overlay mutated source tiles.");
            Dialog(dialog => { dialog.RedValue.Text = "NaN"; Check(!dialog.Preview(), "NaN accepted."); dialog.ApplyEdit(); Check(dialog.IsVisible, "Invalid Apply closed dialog."); });
            Check(session.ActiveLayer!.Effects!.ColorOverlay == first, "Invalid/cancel lost settings.");
            Dialog(dialog =>
            {
                dialog.SetSettings(second); dialog.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.ChangeExtension(reportPath, ".dialog.png"))) encoder.Save(file);
                dialog.ApplyEdit();
            });
            Undo(this, new()); Check(session.ActiveLayer!.Effects!.ColorOverlay == first, "Undo failed.");
            int undo = session.UndoCount, redo = session.RedoCount;
            Dialog(dialog => dialog.ApplyEdit()); Check(session.UndoCount == undo && session.RedoCount == redo, "No-op lost history.");
            Dialog(dialog => { dialog.SetSettings(second); dialog.Close(); }); Check(session.RedoCount == redo, "Cancel lost Redo.");
            Redo(this, new()); Check(session.ActiveLayer!.Effects!.ColorOverlay == second, "Redo failed.");
            Dialog(dialog => { dialog.EffectEnabled.IsChecked = false; dialog.ApplyEdit(); });
            Check(session.ActiveLayer!.Effects!.ColorOverlay!.Enabled == false && CompositePixels(session.Document).SequenceEqual(source.Pixels.ToRgba()), "Disable lost settings or changed plain source.");
            Dialog(dialog => dialog.ApplyEdit()); Check(session.ActiveLayer!.Effects!.ColorOverlay!.Enabled == false, "Disabled effect not retained.");
            Undo(this, new()); Check(session.ActiveLayer!.Effects!.ColorOverlay == second, "Disable Undo failed.");
            Dialog(dialog => { dialog.OpacityValue.Text = ".35"; dialog.ApplyEdit(); }); Check(session.ActiveLayer!.Effects!.ColorOverlay!.Opacity == .35, "Immediate input not applied.");
            var savedEffect = session.ActiveLayer.Effects.ColorOverlay;
            Dialog(dialog => { dialog.ResetButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); Check(session.ActiveLayer!.Effects!.ColorOverlay == new ColorOverlayEffect(1, 1, 1), "Reset failed."); dialog.Close(); });
            Dialog(dialog => { dialog.RedValue.Text = "invalid"; dialog.RemoveEdit(); }); Check(session.ActiveLayer!.Effects!.ColorOverlay is null, "Remove failed.");
            Undo(this, new()); Check(session.ActiveLayer!.Effects!.ColorOverlay == savedEffect, "Remove Undo failed.");
            AddRevealMask(this, new()); ToolPicker.SelectedIndex = 1; MaskGray.Text = "0"; BrushSize.Text = "180"; BrushHardness.Text = "60";
            Canvas.BeginPointer(new(200, 200)); Canvas.MovePointer(new(600, 400)); Canvas.EndPointer(true);
            Check(ReferenceEquals(source.Pixels, session.ActiveLayer!.Pixels) && session.ActiveLayer.Effects!.ColorOverlay == savedEffect, "Mask paint changed source/effect.");
            EditTarget.SelectedIndex = 0; Canvas.BeginPointer(new(250, 250)); Canvas.MovePointer(new(500, 300)); Canvas.EndPointer(true);
            Check(!ReferenceEquals(source.Pixels, session.ActiveLayer!.Pixels) && session.ActiveLayer.Effects!.ColorOverlay == savedEffect, "Image paint did not remain live.");
            projectPath = Path.Combine(root, "Overlay.comp"); Check(await Save(false), "UI save failed."); byte[] expected = CompositePixels(session.Document);
            Check(await LoadProject(projectPath, false), "UI reopen failed."); Check(expected.SequenceEqual(CompositePixels(session.Document)) && session.ActiveLayer!.Effects!.ColorOverlay == savedEffect, "Reopen changed output/settings.");
            Dialog(dialog => { dialog.SetSettings(first); dialog.ApplyEdit(); }); Undo(this, new()); Check(!session.IsModified && expected.SequenceEqual(CompositePixels(session.Document)), "Reopened edit Undo failed.");
            string png = Path.Combine(root, "Overlay.png"); ImageCodec.Export(session.Document, png, false); Check(expected.SequenceEqual(ImageCodec.Load(png).ToRgba()), "PNG output differs.");
            WrapInGroup(this, new()); Check(!ColorOverlayMenu.IsEnabled && !ColorOverlayButton.IsEnabled, "Group effects enabled.");
            bool rejected = false; try { _ = new ColorOverlayWindow(session); } catch (InvalidOperationException) { rejected = true; } Check(rejected, "Group effect dialog allowed.");
            Undo(this, new()); session.Apply(d => d with { Layers = d.Layers.Add(Layer.ExposureLayer(800, 600)) }); session.ActiveLayerId = session.Document.Layers[^1].Id; Refresh();
            Check(!ColorOverlayMenu.IsEnabled, "Adjustment effects enabled."); Undo(this, new()); session.ActiveLayerId = source.Id; Refresh(); Canvas.Fit();
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow,
                checks = new[] { "create/cancel preserves source/selection", "one Undo, no extra layer", "original comparison", "invalid input remains open", "edit/undo/redo", "no-op/cancel preserves redo", "enable/disable persists", "immediate apply", "reset/remove/undo", "live mask and image brush", "UI save/reopen/re-edit", "PNG matches composite", "group/adjustment guard" },
                note = "Hidden WPF controls; physical input and actual Mac round trip not verified." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Canvas.CancelInteraction(); projectPath = null; Refresh();
            string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetDirectoryName(reportPath)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("overlay-smoke-")) throw new IOException("Unsafe cleanup.");
            Directory.Delete(full, true);
        }
    }
}
