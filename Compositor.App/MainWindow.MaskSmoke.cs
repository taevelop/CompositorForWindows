using System.IO;
using System.Text.Json;
using System.Windows;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public partial class MainWindow
{
    private async Task MaskSmokeTest(string reportPath)
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "mask-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        try
        {
            var doc = Document.Create(800, 600);
            var fill = new BrushStroke(doc.Layers[0], new(2000, 1, 1, 98, 201, 181), 800, 600); fill.Append(new(400, 300));
            session.Load(doc.Replace(doc.Layers[0] with { Pixels = fill.Pixels }));
            var image = session.ActiveLayer!.Pixels; AddRevealMask(this, new());
            Check(session.EditMask && EditTarget.SelectedIndex == 1 && session.ActiveLayer!.Mask is not null, "Add mask did not select mask editing.");
            ToolPicker.SelectedIndex = 1; MaskGray.Text = "0"; BrushSize.Text = "150"; BrushHardness.Text = "60";
            Canvas.BeginPointer(new(200, 250)); Canvas.MovePointer(new(600, 350)); Canvas.EndPointer(true);
            var painted = session.Document; var composite = CompositePixels(painted);
            Check(ReferenceEquals(image, session.ActiveLayer!.Pixels) && composite[(300 * 800 + 400) * 4 + 3] == 0, "Mask painting did not hide source pixels.");
            Undo(this, new()); Check(session.ActiveLayer!.Mask!.Pixels.Width == 1 && session.EditMask, "Undo did not restore the uniform mask.");
            Redo(this, new()); Check(ReferenceEquals(painted, session.Document), "Redo did not restore painted mask.");
            Canvas.BeginPointer(new(100, 100)); Canvas.MovePointer(new(500, 100)); Canvas.CancelInteraction();
            Check(ReferenceEquals(painted, session.Document), "Mask cancellation changed the document.");
            MaskEnabled.IsChecked = false; ToggleMask(this, new()); Check(!session.ActiveLayer!.Mask!.Enabled, "Mask disable failed.");
            Check(CompositePixels(session.Document)[(300 * 800 + 400) * 4 + 3] == 255, "Disabled mask still hid pixels.");
            MaskEnabled.IsChecked = true; ToggleMask(this, new());
            RemoveMask(this, new()); Check(session.ActiveLayer!.Mask is null && !session.EditMask, "Mask removal did not select image.");
            Undo(this, new()); Check(session.ActiveLayer!.Mask is not null && session.EditMask, "Undo remove lost mask target.");
            EditTarget.SelectedIndex = 0; Check(!session.EditMask, "Image target selection failed.");
            EditTarget.SelectedIndex = 1; MaskGray.Text = "100";
            Canvas.BeginPointer(new(400, 300)); Canvas.EndPointer(true);
            Check(CompositePixels(session.Document)[(300 * 800 + 400) * 4 + 3] == 255, "White mask brush did not reveal pixels.");
            ToolPicker.SelectedIndex = 2; Canvas.BeginPointer(new(400, 300)); Canvas.EndPointer(true);
            Check(CompositePixels(session.Document)[(300 * 800 + 400) * 4 + 3] == 0 && ReferenceEquals(image, session.ActiveLayer!.Pixels), "Mask eraser changed source or failed to hide.");
            projectPath = Path.Combine(root, "Masked.comp"); Check(await Save(false), "Masked UI save failed.");
            byte[] expected = CompositePixels(session.Document);
            Check(await LoadProject(projectPath, false), "Masked UI reopen failed.");
            Check(expected.SequenceEqual(CompositePixels(session.Document)) && !session.IsModified, "Masked reopen changed the composite.");
            string png = Path.Combine(root, "Masked.png"); ImageCodec.Export(session.Document, png, false);
            Check(expected.SequenceEqual(ImageCodec.Load(png).ToRgba()), "Masked export differs from UI composition.");
            EditTarget.SelectedIndex = 1; ToolPicker.SelectedIndex = 1; Canvas.Fit();
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow,
                checks = new[] { "add/select mask", "black/white brush and eraser", "source pixels preserved", "cancel/undo/redo", "enable/remove/undo", "UI save/reopen", "PNG export equals composite" },
                note = "Hidden WPF routes; no physical mouse or real Mac application exercised." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Canvas.CancelInteraction(); projectPath = null; Refresh();
            string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetDirectoryName(reportPath)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("mask-smoke-")) throw new IOException("Unsafe mask smoke cleanup.");
            Directory.Delete(full, true);
        }
    }
}
