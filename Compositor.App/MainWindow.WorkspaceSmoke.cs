using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task<bool> ReopenSavedProject(string path)
    {
        if(session.IsModified)throw new InvalidOperationException("Roundtrip fixture must be saved before closing.");
        var id=workspace.Current.Id;
        if(!PrepareTabChange()||!workspace.Close(id))return false;
        tabViews.Remove(id);BindCurrentTab();return await LoadProject(path,false);
    }
    private async Task WorkspaceSmokeTest(string path)
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var first=workspace.Current;var original=session.Document;var savedPrompts=prompts;var originalTools=CaptureTabTools();
        var directory=Path.Combine(Path.GetTempPath(),"Compositor-workspace-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            session.Apply(d=>d.Replace(d.Layers[0] with{Name="First tab edits"}));var edited=session.Document;
            Canvas.ActualPixels();Canvas.ZoomAt(new(140,110),120);var view=Canvas.CaptureView();
            var firstTools=defaultTabTools with{Tool=4,Size="127",Hardness="31",Opacity="62",Color="#336699",Mask="27",Mode=Compositor.Core.SelectionMode.Add,Antialiased=false,Center=true,CropRatio=3,CropSnap=false};
            RestoreTabTools(firstTools);
            AddDocumentTab(Document.Create(64,48));var second=workspace.Current;
            Check(CaptureTabTools()==defaultTabTools,"New tab inherited another document's tools.");
            var secondTools=defaultTabTools with{Tool=2,Size="81",Color="#CC6633"};RestoreTabTools(secondTools);
            Check(first!=second&&ReferenceEquals(Canvas.Session,second.Session),"New tab did not bind its own session.");
            session.Apply(d=>d.Replace(d.Layers[0] with{Name="Second tab edits"}));var secondDocument=session.Document;
            SelectTab(first.Id);Check(ReferenceEquals(edited,session.Document)&&Canvas.CaptureView()==view,"Switch lost document or viewport.");
            Check(CaptureTabTools()==firstTools&&Canvas.CropDraft is null,"Tab tools or crop state leaked across documents.");
            Check(await HandleDocumentShortcut(System.Windows.Input.Key.Tab,System.Windows.Input.ModifierKeys.Control)&&workspace.Current==second&&CaptureTabTools()==secondTools,"Next-tab shortcut failed.");
            BrushSize.Focus();
            Check(await HandleDocumentShortcut(System.Windows.Input.Key.Tab,System.Windows.Input.ModifierKeys.Control|System.Windows.Input.ModifierKeys.Shift)&&workspace.Current==first,"Previous-tab shortcut failed with a focused number field.");
            session.Undo();Check(ReferenceEquals(original,session.Document)&&ReferenceEquals(secondDocument,second.Session.Document),"Undo affected another tab.");
            SelectTab(second.Id);projectPath=Path.Combine(directory,"Second.comp");Check(await Save(false),"Tab save failed.");
            var undo=session.UndoCount;Check(await LoadProject(projectPath,false)&&workspace.Current==second&&session.UndoCount==undo,"Duplicate open replaced history.");
            Check(await ReopenSavedProject(projectPath!)&&!session.CanUndo&&session.Document.Layers[0].Name=="Second tab edits","Tab roundtrip failed.");
            var reopened=workspace.Current;
            first.Session.Apply(d=>d.Replace(d.Layers[0] with{Name="Background unsaved"}));
            prompts=new(()=>workspace.Current==first?MessageBoxResult.Cancel:MessageBoxResult.No,message=>throw new InvalidOperationException(message));
            Check(!await ConfirmWorkspaceClose()&&workspace.Current==first&&first.Session.IsModified,"Quit skipped background unsaved document.");
            SelectTab(reopened.Id);
            session.Apply(d=>d.Replace(d.Layers[0] with{Name="Unsaved"}));
            prompts=new(()=>MessageBoxResult.Cancel,message=>throw new InvalidOperationException(message));
            await HandleDocumentShortcut(System.Windows.Input.Key.W,System.Windows.Input.ModifierKeys.Control);Check(workspace.Documents.Contains(reopened)&&session.IsModified,"Cancelled tab close lost edits.");
            UpdateLayout();var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
            var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))png.Save(output);
            prompts=new(()=>MessageBoxResult.No,message=>throw new InvalidOperationException(message));await CloseTab(reopened.Id);
            Check(!workspace.Documents.Contains(reopened),"Discard did not close tab.");SelectTab(first.Id);
        }
        finally
        {
            prompts=savedPrompts;SelectTab(first.Id);session.Load(original);RestoreTabTools(originalTools);
            var root=Path.GetFullPath(directory);var temp=Path.GetFullPath(Path.GetTempPath());
            if(root.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("Compositor-workspace-"))Directory.Delete(root,true);
        }
    }
}
