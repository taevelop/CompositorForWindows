using System.IO;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;

public partial class MainWindow
{
    private async Task MaskCopySmoke(string report)
    {
        void Check(bool ok,string message){if(!ok)throw new InvalidOperationException(message);}
        var saved=session.Document;var active=session.ActiveLayerId;bool editMask=session.EditMask;
        string root=Path.Combine(Path.GetDirectoryName(Path.GetFullPath(report))!,"mask-copy-smoke-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            var doc=Document.Create(64,64);var source=doc.Layers[0] with{Pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,.2,.4,.6),4,4),Transform=new(10,10,4,4),Mask=LayerMask.Solid(4,4,96) with{Linked=false}};
            var target=source with{Id=Guid.NewGuid(),Name="Target",Transform=new(30,30,8,8),Mask=LayerMask.Solid(4,4)};doc=doc.Replace(source) with{Layers=doc.Replace(source).Layers.Add(target)};session.Load(doc,source.Id);
            var payload=new MaskDragPayload(doc,source.Id);Check(CommitMaskCopy(payload,target.Id),"Mask copy did not commit.");
            var mask=session.ActiveLayer!.Mask!;Check(session.ActiveLayerId==target.Id&&session.EditMask&&session.UndoCount==1&&ReferenceEquals(mask.Pixels,source.Mask!.Pixels)&&mask.Placement==source.Transform&&!mask.Linked,"Copied mask lost pixels/placement/link or target selection.");
            var copied=session.Document;var output=CompositePixels(copied);Undo(this,new());Check(ReferenceEquals(session.Document,doc),"Mask copy Undo lost source.");Redo(this,new());
            projectPath=Path.Combine(root,"Copied.comp");Check(await Save(false)&&await ReopenSavedProject(projectPath)&&output.SequenceEqual(CompositePixels(session.Document)),"Mask copy save/reopen changed output.");
            Check(session.ActiveLayer!.Mask!.Placement==source.Transform&&!session.ActiveLayer.Mask.Linked,"Mask copy save/reopen lost placement/link.");
            var before=session.Document;Check(!CommitMaskCopy(payload,target.Id)&&ReferenceEquals(session.Document,before),"Stale mask payload changed reopened document.");
            Check(!CommitMaskCopy(new(before,target.Id),target.Id)&&ReferenceEquals(session.Document,before),"Self mask drop changed the document.");
            File.WriteAllText(report,JsonSerializer.Serialize(new{checks=new[]{"mask copy target selection/Undo","shared mask pixels/document placement/link","mask save/output roundtrip","stale payload rejected","self drop no-op"},note="WPF commit path and core tests; actual OLE Alt-drag physical input is not verified."}));
        }
        finally
        {
            projectPath=null;session.Load(saved,active);session.EditMask=editMask;Refresh();
            string parent=Path.GetDirectoryName(Path.GetFullPath(report))!+Path.DirectorySeparatorChar;
            if(Path.GetFullPath(root).StartsWith(parent,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(root).StartsWith("mask-copy-smoke-",StringComparison.Ordinal))Directory.Delete(root,true);
        }
    }
}
