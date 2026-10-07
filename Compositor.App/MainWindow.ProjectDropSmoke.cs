using System.IO;
using System.Windows;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task ProjectDropSmokeTest()
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var source=workspace.Current;var original=session.Document;var existing=workspace.Documents.Select(t=>t.Id).ToHashSet();var savedPrompts=prompts;
        string root=Path.Combine(Path.GetTempPath(),"Compositor-project-drop-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var errors=new List<string>();prompts=new(()=>MessageBoxResult.Cancel,errors.Add);
            string project=Path.Combine(root,"Sample.comp"),image=Path.Combine(root,"Image.png");
            ProjectStore.Save(Document.Create(6,5),null,project);ImageCodec.Export(Document.Create(2,3),image,false);
            AddDocumentTab(Document.Create(20,15));var target=workspace.Current;
            Canvas.RestoreView(2.5,-20,30);
            await DropCanvasFiles(new[]{project+Path.DirectorySeparatorChar,image},new Point(91,73));
            var opened=workspace.FindPath(project)!;
            Check(opened is not null&&opened.Session.Document.Width==6&&opened.Session.Document.Layers.Length==1,"Project drop imported images into newly opened project.");
            Check(workspace.Current==target&&target.Session.Document.Layers.Length==2,"Mixed drop lost original image destination.");
            Check(ReferenceEquals(original,source.Session.Document),"Project drop changed unrelated document.");
            Check(target.Session.ActiveLayer!.Transform.X==43&&target.Session.ActiveLayer.Transform.Y==15,"Canvas drop lost zoom/pan position or pixel snapping after opening project.");
            SelectTab(opened!.Id);session.Apply(d=>d.Replace(d.Layers[0] with{Name="Keep edits"}));var edited=session.Document;int count=workspace.Documents.Count;
            await RouteDroppedFiles(null,new[]{project});Check(workspace.Documents.Count==count&&ReferenceEquals(edited,session.Document),"Repeated project drop replaced existing edits.");
            string corrupt=Path.Combine(root,"Corrupt.comp");Directory.CreateDirectory(corrupt);File.WriteAllText(Path.Combine(corrupt,"manifest.json"),"{bad");
            await RouteDroppedFiles(null,new[]{corrupt});Check(errors.Count==1&&workspace.Documents.Count==count&&ReferenceEquals(edited,session.Document),"Corrupt dropped project changed workspace.");
            string recoverySource=Path.GetFullPath(Path.Combine(root,"Recovery.comp")),recovery=recoverySource+".recovery";
            string boundary=Path.GetFullPath(root)+Path.DirectorySeparatorChar;
            if(!recoverySource.StartsWith(boundary,StringComparison.OrdinalIgnoreCase)||!recovery.StartsWith(boundary,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Recovery test path escaped its directory.");
            ProjectStore.Save(Document.Create(7,9),null,recoverySource);Directory.Move(recoverySource,recovery);
            byte[] manifest=File.ReadAllBytes(Path.Combine(recovery,"manifest.json"));
            await RouteDroppedFiles(null,new[]{recovery});
            Check(workspace.Current.ProjectPath is null&&session.IsModified&&session.Document.Width==7,"Recovery drop did not open an unsaved copy.");
            Check(manifest.SequenceEqual(File.ReadAllBytes(Path.Combine(recovery,"manifest.json"))),"Recovery drop modified its source.");
        }
        finally
        {
            prompts=savedPrompts;SelectTab(source.Id);
            foreach(var id in workspace.Documents.Select(t=>t.Id).Where(id=>!existing.Contains(id)).ToArray()){workspace.Close(id,true);tabViews.Remove(id);}Refresh();
            var resolved=Path.GetFullPath(root);
            if(resolved.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(resolved).StartsWith("Compositor-project-drop-"))Directory.Delete(resolved,true);
        }
    }
}
