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
        var source=workspace.Current;var sourceDocument=session.Document;var savedPrompts=prompts;Guid? added=null;
        string root=Path.Combine(Path.GetTempPath(),"Compositor-tab-drop-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var errors=new List<string>();prompts=new(()=>MessageBoxResult.Cancel,errors.Add);
            string path=Path.Combine(root,"Incoming.png");ImageCodec.Export(Document.Create(4,3),path,false);
            AddDocumentTab(Document.Create(20,15));var target=workspace.Current;added=target.Id;var before=session.Document;
            SelectTab(source.Id);await ImportIntoTab(target.Id,new[]{path});
            Check(workspace.Current==target&&session.Document.Layers.Length==before.Layers.Length+1,"File drop ignored destination tab.");
            Check(ReferenceEquals(sourceDocument,source.Session.Document),"File drop changed source document.");
            Check(session.UndoCount==1&&session.ActiveLayer!.Name=="Incoming","File drop split history or failed to select imported layer.");
            session.Undo();Check(ReferenceEquals(before,session.Document),"Destination import Undo failed.");
            SelectTab(source.Id);await ImportIntoTab(target.Id,new[]{path,Path.Combine(root,"missing.png")});
            Check(errors.Count==1&&ReferenceEquals(before,target.Session.Document),"Failed batch drop partially imported images.");
            SelectTab(source.Id);busy=true;try{await ImportIntoTab(target.Id,new[]{path});}finally{busy=false;}
            Check(workspace.Current==source&&ReferenceEquals(before,target.Session.Document),"Busy editor accepted a tab drop.");
        }
        finally
        {
            prompts=savedPrompts;SelectTab(source.Id);
            if(added is {} id){workspace.Close(id,true);tabViews.Remove(id);Refresh();}
            string resolved=Path.GetFullPath(root);
            if(resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("Compositor-tab-drop-"))Directory.Delete(resolved,true);
        }
    }
}
