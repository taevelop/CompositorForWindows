using System.IO;
using System.Windows;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task TabDropSmokeTest()
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var source=workspace.Current;var sourceDocument=session.Document;var savedPrompts=prompts;var existing=workspace.Documents.Select(t=>t.Id).ToHashSet();
        string root=Path.Combine(Path.GetTempPath(),"Compositor-tab-drop-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var errors=new List<string>();prompts=new(()=>MessageBoxResult.Cancel,errors.Add);
            string path=Path.Combine(root,"Incoming.png");ImageCodec.Export(Document.Create(4,3),path,false);
            AddDocumentTab(Document.Create(20,15));var target=workspace.Current;var before=session.Document;
            SelectTab(source.Id);await ImportIntoTab(target.Id,new[]{path});
            Check(workspace.Current==target&&session.Document.Layers.Length==before.Layers.Length+1,"File drop ignored destination tab.");
            Check(ReferenceEquals(sourceDocument,source.Session.Document),"File drop changed source document.");
            Check(session.UndoCount==1&&session.ActiveLayer!.Name=="Incoming","File drop split history or failed to select imported layer.");
            session.Undo();Check(ReferenceEquals(before,session.Document),"Destination import Undo failed.");
            SelectTab(source.Id);await ImportIntoTab(target.Id,new[]{path,Path.Combine(root,"missing.png")});
            Check(errors.Count==1&&ReferenceEquals(before,target.Session.Document),"Failed batch drop partially imported images.");
            SelectTab(source.Id);busy=true;try{await ImportIntoTab(target.Id,new[]{path});}finally{busy=false;}
            Check(workspace.Current==source&&ReferenceEquals(before,target.Session.Document),"Busy editor accepted a tab drop.");
            int count=workspace.Documents.Count;
            await OpenImagesInTabs(new[]{path,Path.Combine(root,"missing-again.png"),path});
            Check(workspace.Documents.Count==count+2&&errors.Count==2,"New-image drop created an empty failed tab or omitted a valid image.");
            foreach(var tab in workspace.Documents.Skip(count))
            {
                var image=tab.Session.Document;
                Check(image.Width==4&&image.Height==3&&image.Layers.Length==1,"Image canvas dimensions or layer count incorrect.");
                Check(tab.ProjectPath is null&&tab.Session.IsModified&&tab.Session.UndoCount==1,"Image document did not retain unsaved import history.");
                Check(image.Layers[0].Transform.X==0&&image.Layers[0].Transform.Y==0&&image.Layers[0].Name=="Incoming","New canvas image placement incorrect.");
            }
            Check(ReferenceEquals(sourceDocument,source.Session.Document),"New-image drop changed original document.");
            var imported=session.Document;session.Undo();session.Redo();Check(ReferenceEquals(imported,session.Document),"New-image import redo changed snapshot.");
        }
        finally
        {
            prompts=savedPrompts;SelectTab(source.Id);
            foreach(var id in workspace.Documents.Select(t=>t.Id).Where(id=>!existing.Contains(id)).ToArray()){workspace.Close(id,true);tabViews.Remove(id);}Refresh();
            string resolved=Path.GetFullPath(root);
            if(resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("Compositor-tab-drop-"))Directory.Delete(resolved,true);
        }
    }
}
