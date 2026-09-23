using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

public partial class MainWindow
{
    private async Task GroupSmokeTest(string reportPath)
    {
        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(reportPath))!, "group-smoke-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var savedPrompts = prompts;
        prompts = new(() => MessageBoxResult.Cancel, message => throw new InvalidOperationException("Unexpected group dialog: " + message));
        void Check(bool ok, string message) { if (!ok) throw new InvalidOperationException(message); }
        void Select(Guid id)
        {
            Layers.SelectedItem = Layers.Items.Cast<LayerRow>().Single(r => r.Id == id);
            Check(session.ActiveLayerId == id, "Hierarchy selection did not change the active layer.");
        }
        try
        {
            var original = session.Document; var originalChild = session.ActiveLayer!;
            WrapInGroup(this, new()); var group = session.ActiveLayer!;
            Check(group.IsGroup && session.Document.Layers.Single(l => l.Id == originalChild.Id).ParentId == group.Id, "Wrapping failed.");
            Check(!TransformPanel.IsEnabled && !RevealMaskButton.IsEnabled, "Group exposed unsupported transforms or masks.");
            ToolPicker.SelectedIndex = 1; bool groupPaintRejected = false;
            try { Canvas.BeginPointer(new(100, 100)); } catch (InvalidOperationException) { groupPaintRejected = true; }
            Check(groupPaintRejected && !session.InTransaction && !Canvas.HasInteraction, "Group painting was not safely rejected.");
            Check(CompositePixels(original).SequenceEqual(CompositePixels(session.Document)), "Wrapping changed appearance.");
            LayerName.Text = "Artwork"; LayerOpacity.Text = "50"; ApplyLayer(this, new()); group = session.ActiveLayer!;
            var expected = original.Replace(originalChild with { Opacity = originalChild.Opacity * .5 });
            Check(CompositePixels(expected).SequenceEqual(CompositePixels(session.Document)), "Group opacity was not inherited.");
            AddGroup(this, new()); var nested = session.ActiveLayer!; Check(nested.ParentId == group.Id, "Nested group has wrong parent.");
            AddLayer(this, new()); var paintLayer = session.ActiveLayer!; Check(paintLayer.ParentId == nested.Id, "New layer was not inserted into the group.");
            ToolPicker.SelectedIndex = 1; BrushColor.Text = "#ECA450"; BrushSize.Text = "90";
            Canvas.BeginPointer(new(250, 170)); Canvas.MovePointer(new(600, 170)); Canvas.EndPointer(true);
            Check(session.ActiveLayer!.Pixels.Tiles.Count > 0, "Child painting failed.");

            int undoCount = session.UndoCount; var beforeCollapse = session.Document;
            ToggleGroup(new Button { Tag = group.Id }, new RoutedEventArgs(Button.ClickEvent));
            Check(Layers.Items.Count == 1 && session.ActiveLayerId == group.Id, "Collapse did not hide descendants or select parent.");
            Check(session.UndoCount == undoCount && ReferenceEquals(beforeCollapse, session.Document), "Expansion changed document history.");
            ToggleGroup(new Button { Tag = group.Id }, new RoutedEventArgs(Button.ClickEvent)); Check(Layers.Items.Count == 4, "Expand failed.");
            Select(group.Id); ToolPicker.SelectedIndex = 0;
            Canvas.BeginPointer(new(0, 0)); Canvas.MovePointer(new(20, 10)); Canvas.EndPointer(true);
            Check(session.Document.Layers.Single(l => l.Id == originalChild.Id).Transform.X == originalChild.Transform.X + 20, "Group move did not move child.");
            Check(session.Document.Layers.Single(l => l.Id == paintLayer.Id).Transform.X == paintLayer.Transform.X + 20, "Group move did not move nested child.");
            Undo(this, new()); Check(ReferenceEquals(beforeCollapse, session.Document), "Group move undo did not restore snapshot.");
            Canvas.BeginPointer(new(0, 0)); Canvas.MovePointer(new(50, 60)); Canvas.CancelInteraction();
            Check(ReferenceEquals(beforeCollapse, session.Document), "Group move cancellation failed.");

            Select(originalChild.Id);
            ParentGroup.SelectedItem = ParentGroup.Items.Cast<ParentOption>().Single(p => p.Id == nested.Id); MoveToGroup(this, new());
            Check(session.ActiveLayer!.ParentId == nested.Id, "Move into group failed.");
            MoveOutOfGroup(this, new()); Check(session.ActiveLayer!.ParentId == group.Id, "Move out failed.");
            LayerUp(this, new()); Check(LayerHierarchy.Entries(session.Document).Where(e => e.Layer.ParentId == group.Id).Last().Layer.Id == originalChild.Id, "Sibling reorder failed.");
            ParentGroup.SelectedItem = ParentGroup.Items.Cast<ParentOption>().Single(p => p.Id is null); MoveToGroup(this, new());
            ToggleGroup(new Button { Tag = group.Id }, new RoutedEventArgs(Button.ClickEvent));
            Undo(this, new()); Check(session.ActiveLayer!.ParentId == group.Id && Layers.Items.Cast<LayerRow>().Any(r => r.Id == originalChild.Id), "Undo reparent did not reveal selection.");

            Select(group.Id); var grouped = session.Document; byte[] pixels = CompositePixels(grouped);
            Ungroup(this, new()); Check(CompositePixels(session.Document).SequenceEqual(pixels), "Ungroup changed the composite.");
            Undo(this, new()); Check(session.ActiveLayerId == group.Id, "Ungroup undo did not restore selection.");
            DeleteLayer(this, new()); Check(session.Document.Layers.Length == 0, "Delete group left descendants behind.");
            Undo(this, new()); Check(ReferenceEquals(grouped, session.Document), "Delete group undo lost children.");
            LayerVisible.IsChecked = false; ApplyLayer(this, new());
            Check(CompositePixels(session.Document).All(b => b == 0), "Hidden parent did not hide children.");
            Select(originalChild.Id); ToolPicker.SelectedIndex = 1; bool hiddenPaintRejected = false;
            try { Canvas.BeginPointer(new(100, 100)); } catch (InvalidOperationException) { hiddenPaintRejected = true; }
            Check(hiddenPaintRejected && !session.InTransaction, "Painting under a hidden group was accepted.");
            Select(group.Id); ToolPicker.SelectedIndex = 0;
            Undo(this, new()); Check(CompositePixels(session.Document).SequenceEqual(pixels), "Visibility undo changed output.");

            projectPath = Path.Combine(root, "Groups.comp"); Check(await Save(false), "Grouped project UI save failed.");
            Check(await LoadProject(projectPath, false), "Grouped project UI load failed.");
            Check(session.ActiveLayer!.IsGroup && CompositePixels(session.Document).SequenceEqual(pixels), "Grouped reopen changed selection or pixels.");
            string png = Path.Combine(root, "Groups.png"); ImageCodec.Export(session.Document, png, false);
            Check(ImageCodec.Load(png).ToRgba().SequenceEqual(pixels), "Grouped export differs from composition.");
            Select(nested.Id);
            string input = Path.Combine(root, "Import.png"); ImageCodec.Export(Document.Create(16, 16), input, false);
            await Import([input]); Check(session.ActiveLayer!.ParentId == nested.Id, "Image import ignored selected group.");
            Undo(this, new()); Check(!session.IsModified, "Undo import did not return to saved revision.");
            Select(group.Id); ToolPicker.SelectedIndex = 0; Canvas.Fit();
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { timeUtc = DateTimeOffset.UtcNow,
                checks = new[] { "wrap/nested group and add child", "hierarchy selection and collapse", "group opacity and visibility", "move subtree/cancel/undo", "reparent/reorder/undo reveals selection", "ungroup preserves appearance", "delete subtree and undo", "save/reopen/PNG export", "group-aware import", "reject group and hidden-child painting" },
                note = "Hidden WPF handlers; no physical pointer or Mac application exercised." }, new JsonSerializerOptions { WriteIndented = true }));
        }
        finally
        {
            Canvas.CancelInteraction(); prompts = savedPrompts; projectPath = null; Refresh();
            string full = Path.GetFullPath(root), parent = Path.GetFullPath(Path.GetDirectoryName(reportPath)!).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("group-smoke-")) throw new IOException("Unsafe group smoke cleanup.");
            Directory.Delete(full, true);
        }
    }
}
