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
    private async Task CurvesSmokeTest(string reportPath)
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "curves-smoke-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        void Check(bool valid, string message) { if (!valid) throw new InvalidOperationException(message); }
        void Dialog(bool create, Action<CurvesWindow> action)
        {
            var dialog = new CurvesWindow(session, create) { Owner = this }; Exception? failure = null;
            dialog.Loaded += (_, _) => { try { action(dialog); } catch (Exception e) { failure = e; } finally { if (dialog.IsVisible) dialog.Close(); } };
            try { dialog.ShowDialog(); } finally { dialog.CancelEdit(); Refresh(); }
            if (failure is not null) throw new InvalidOperationException("Curves dialog failed.", failure);
        }
        try
        {
            var first = new CurvesAdjustment { RGB = new(new(0, 0), new(100, 160), new(255, 255)) }; var second = first with { Channel = LevelsChannel.Blue, Blue = new(new(0, 0), new(120, 80), new(255, 255)) };
            var doc = Document.Create(800, 600); var fill = new BrushStroke(doc.Layers[0], new(2000, 1, .7, 70, 120, 180), 800, 600); fill.Append(new(400, 300));
            var source = doc.Layers[0] with { Pixels = fill.Pixels }; doc = doc.Replace(source); session.Load(doc); Canvas.Fit();
            Dialog(true, dialog => { dialog.SetSettings(first); Check(session.ActiveLayer!.Curves is not null, "New preview not selected."); dialog.Close(); });
            Check(ReferenceEquals(doc, session.Document) && session.ActiveLayerId == source.Id && !session.IsModified, "Cancel create lost document or selection.");
            Dialog(true, dialog => { dialog.SetSettings(first); dialog.SetPreviewVisible(false); Check(ReferenceEquals(doc, session.Document), "Original comparison failed."); dialog.ApplyEdit(); });
            var adjustment = session.ActiveLayer!; Check(adjustment.Curves == first && session.UndoCount == 1, "Create was not one Undo.");
            Check(ReferenceEquals(source.Pixels, session.Document.Layers[0].Pixels) && !AdjustColorsButton.IsEnabled && EditCurvesMenu.IsEnabled, "Curves source/UI guard failed.");
            Dialog(false, dialog => { dialog.SetSettings(second); dialog.OutputValue.Text = "NaN"; Check(!dialog.Preview(), "Invalid curves accepted."); dialog.ApplyEdit(); Check(dialog.IsVisible, "Invalid Apply closed the window."); });
            Check(session.ActiveLayer!.Curves == adjustment.Curves, "Cancel edit lost saved settings.");
            Dialog(false, dialog =>
            {
                dialog.Graph.SelectPoint(1); dialog.OutputValue.Text = "190";
                dialog.ChannelPicker.SelectedItem = LevelsChannel.Red;
                Check(session.ActiveLayer!.Curves!.RGB.Points[1].Y == 190, "Channel switch lost pending numeric edit.");
                Check(dialog.Graph.BeginPoint(100, 140), "Graph insertion failed."); dialog.Graph.MovePoint(110, 150); dialog.Graph.EndPoint(true);
                Check(dialog.Preview() && session.ActiveLayer!.Curves!.Red.Points[1] == new CurvePoint(110, 150), "Graph drag did not update settings.");
                var beforeDrag = dialog.Graph.Curve; dialog.Graph.BeginPoint(110, 150); dialog.Graph.MovePoint(200, 30); dialog.Graph.EndPoint(false);
                Check(dialog.Graph.Curve.Equals(beforeDrag), "Cancelled graph drag changed curve.");
                dialog.Graph.BeginPoint(110, 150); dialog.Graph.MovePoint(-10, 150);
                Check(dialog.Graph.Curve.Points[1].X >= 1 && double.IsFinite(dialog.Graph.Curve.Value(0)), "Drag crossed endpoint or produced invalid interpolation.");
                dialog.Graph.EndPoint(false);
                dialog.InputValue.Text = "255"; dialog.ChannelPicker.SelectedItem = LevelsChannel.Green;
                Check((LevelsChannel)dialog.ChannelPicker.SelectedItem == LevelsChannel.Red && dialog.InputValue.Text == "255", "Invalid channel switch discarded input.");
                dialog.ApplyEdit(); Check(dialog.IsVisible, "Invalid point closed dialog.");
                dialog.InputValue.Text = "110"; dialog.OutputValue.Text = "175"; dialog.ApplyEdit();
            });
            Check(session.ActiveLayer!.Curves!.Red.Points[1].Y == 175 && session.ActiveLayer.Curves.RGB.Points[1].Y == 190, "Immediate Apply lost points.");
            Undo(this, new()); Check(session.ActiveLayer!.Curves == adjustment.Curves, "Graph edit was not one Undo.");
            int undoCount = session.UndoCount, redoCount = session.RedoCount;
            Dialog(false, dialog => dialog.ApplyEdit());
            Check(session.UndoCount == undoCount && session.RedoCount == redoCount, "No-op changed history.");
            Dialog(false, dialog =>
            {
                dialog.Graph.SelectPoint(0); Check(!dialog.Graph.DeletePoint(), "Deleted endpoint.");
                dialog.Graph.SelectPoint(1); Check(dialog.Graph.DeletePoint(), "Could not delete interior point.");
                dialog.Close();
            });
            Check(session.RedoCount == redoCount && session.ActiveLayer!.Curves == first, "Cancel lost Redo or points.");
            Dialog(false, dialog =>
            {
                dialog.SetSettings(first with { Red = new(new(0, 0), new(1, 100), new(255, 255)), Channel = LevelsChannel.Red });
                dialog.Graph.SelectPoint(0); dialog.AddPointButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(dialog.Graph.Curve.Points.Length == 4 && dialog.Graph.Curve.Points[1].X == .5, "Midpoint near endpoint selected instead of adding.");
                dialog.ResetChannelButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(session.ActiveLayer!.Curves!.Red.IsIdentity && session.ActiveLayer.Curves.RGB.Equals(first.RGB), "Channel reset changed other channels.");
                dialog.ResetAllButton.RaiseEvent(new System.Windows.RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                Check(session.ActiveLayer!.Curves == new CurvesAdjustment(), "Reset all failed."); dialog.Close();
            });
            Dialog(false, dialog =>
            {
                dialog.SetSettings(second); dialog.UpdateLayout();
                var bitmap = new RenderTargetBitmap((int)dialog.ActualWidth, (int)dialog.ActualHeight, 96, 96, PixelFormats.Pbgra32); bitmap.Render(dialog);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using (var file = File.Create(Path.ChangeExtension(reportPath, ".dialog.png"))) encoder.Save(file);
                dialog.ApplyEdit();
            });
            Undo(this, new()); Check(session.ActiveLayer!.Curves == adjustment.Curves, "Undo curves edit failed."); Redo(this, new());
            AddRevealMask(this, new()); ToolPicker.SelectedIndex = 1; MaskGray.Text = "0"; BrushSize.Text = "180"; BrushHardness.Text = "60";
            Canvas.BeginPointer(new(200, 200)); Canvas.MovePointer(new(600, 400)); Canvas.EndPointer(true);
            Check(session.ActiveLayer!.Mask!.Pixels.Width == 800 && session.ActiveLayer.Pixels.Tiles.Count == 0, "Curves mask painting created source pixels.");
            var masked = session.Document; Canvas.BeginPointer(new(100, 100)); Canvas.CancelInteraction(); Check(ReferenceEquals(masked, session.Document), "Curves mask cancel failed.");
            EditTarget.SelectedIndex = 0;
            bool rejected = false; try { Canvas.BeginPointer(new(100, 100)); } catch (InvalidOperationException) { rejected = true; } finally { Canvas.CancelInteraction(); }
            Check(rejected, "Curves source painting was allowed.");
            projectPath = Path.Combine(root, "Curves.comp"); Check(await Save(false), "Curves UI save failed."); var expected = CompositePixels(session.Document);
            Check(await LoadProject(projectPath, false), "Curves reopen failed."); Check(expected.SequenceEqual(CompositePixels(session.Document)), "Curves reopen changed output.");
            Check(session.ActiveLayer!.Curves == second, "Reopen lost editable settings.");
            Dialog(false, dialog => { dialog.SetSettings(first); dialog.ApplyEdit(); }); Undo(this, new());
            Check(expected.SequenceEqual(CompositePixels(session.Document)) && !session.IsModified, "Reopened edit Undo lost saved state.");
            string png = Path.Combine(root, "Curves.png"); ImageCodec.Export(session.Document, png, false); Check(expected.SequenceEqual(ImageCodec.Load(png).ToRgba()), "Curves PNG differs.");
            WrapInGroup(this, new()); var group = session.ActiveLayer!; Dialog(true, dialog => dialog.ApplyEdit());
            Check(session.ActiveLayer!.ParentId == group.Id, "Curves creation inside a group failed."); Undo(this, new());
            session.ActiveLayerId = adjustment.Id; Refresh(); Canvas.Fit(); await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow,
                checks = new[] { "create/cancel preserves selection", "create/apply one Undo", "original comparison", "invalid values remain open", "graph add/drag/cancel/delete", "close midpoint insertion", "channel/all reset", "channel pending input preserved", "invalid point switch blocked", "no-op/cancel keeps redo", "edit/cancel/undo/redo", "source pixels preserved", "mask brush/cancel", "reject image painting", "UI save/reopen and re-edit", "PNG matches composite", "create inside group" },
                note = "Hidden WPF modal controls; no physical input or real Mac round trip." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Canvas.CancelInteraction(); projectPath = null; Refresh();
            string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetDirectoryName(reportPath)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("curves-smoke-")) throw new IOException("Unsafe cleanup.");
            Directory.Delete(full, true);
        }
    }
}
