using Compositor.Core;
using Compositor.Imaging;
using System.Windows.Input;

namespace Compositor.App;
public partial class MainWindow
{
    private async Task CloneSmoke()
    {
        static void Check(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        var saved = session.Document; var active = session.ActiveLayerId; var tools = CaptureTabTools();
        var first = workspace.Current;
        try
        {
            var doc = Document.Create(8, 4);var bytes = new byte[8 * 4 * 4];bytes[0] = 100;bytes[3] = 255;
            doc = doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(8, 4, bytes) }); session.Load(doc);
            Canvas.CloneSource = new(); ToolPicker.SelectedIndex = 13; BrushSize.Text = "1"; BrushHardness.Text = "100"; BrushOpacity.Text = "100";
            Check(Canvas.Tool == EditorTool.CloneStamp && CloneOptions.IsVisible && !ColorOptions.IsVisible, "Clone controls missing.");
            Canvas.BeginPointer(new(.5, .5), ModifierKeys.Alt); Check(!session.InTransaction && !session.CanUndo, "Setting source edited document.");
            Canvas.BeginPointer(new(4.5, .5), ModifierKeys.None); Canvas.EndPointer(true);
            Check(session.UndoCount == 1 && session.ActiveLayer!.Pixels.ToRgba()[16] == 100, "Clone click did not copy source.");
            session.Undo(); Check(ReferenceEquals(doc, session.Document), "Clone Undo did not restore source."); session.Redo();
            var before = session.Document; Canvas.BeginPointer(new(5.5, .5), ModifierKeys.None); Canvas.CancelInteraction();
            Check(ReferenceEquals(before, session.Document), "Cancelled clone changed document.");
            var source = Canvas.CloneSource; AddDocumentTab(Document.Create(8, 4)); var second = workspace.Current;
            Check(Canvas.CloneSource.Source is null, "New tab inherited clone source.");
            await SelectTabAsync(first.Id); Check(ReferenceEquals(source, Canvas.CloneSource) && ToolPicker.SelectedIndex == 13, "Tab lost clone state.");
            Check(workspace.Close(second.Id, true), "Clone fixture did not close."); tabViews.Remove(second.Id);
        }
        finally { SelectTab(first.Id); session.Load(saved, active); RestoreTabTools(tools); Refresh(); }
    }
}
