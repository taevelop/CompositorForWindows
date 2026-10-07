using System.Windows;
using Compositor.Core;
namespace Compositor.App;
public partial class MainWindow
{
    private void TabLayerTransferSmokeTest()
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var source=workspace.Current;var before=session.Document;var ids=workspace.Documents.Select(t=>t.Id).ToHashSet();
        try
        {
            var doc=Document.Create(20,10);session.Load(doc);var layer=doc.Layers[0];
            AddDocumentTab(Document.Create(100,80));var target=workspace.Current;var targetBefore=session.Document;
            SelectTab(source.Id);var payload=new LayerDragPayload(doc,new[]{layer.Id});layerDragPayload=payload;var data=new DataObject(LayerDragFormat,payload);
            Check(!CanTransferTabLayers(source.Id,data,false)&&!CanTransferTabLayers(target.Id,data,true),"Same-tab or Alt transfer accepted.");
            Check(!CanTransferTabLayers(target.Id,new DataObject(LayerDragFormat,new LayerDragPayload(doc,new[]{layer.Id})),false),"Foreign transfer payload accepted.");
            TransferTabLayers(target.Id,data,false);
            Check(workspace.Current==target&&session.UndoCount==1&&ReferenceEquals(doc,source.Session.Document),"Transfer changed source or split destination history.");
            Check(session.ActiveLayer!.Transform.X==40&&session.ActiveLayer.Transform.Y==35,"Transferred layer did not center in target.");
            session.Undo();Check(ReferenceEquals(targetBefore,session.Document),"Transfer Undo failed.");session.Redo();
            SelectTab(source.Id);layerDragPayload=payload;TransferTabLayers(null,data,false);
            Check(session.Document.Width==20&&session.Document.Height==10&&session.Document.Layers.Length==1&&session.IsModified,"New transfer tab dimensions or state incorrect.");
            Check(session.SelectedLayerIds.Count==1&&!session.Document.Layers.Any(l=>l.Id==layer.Id),"Transfer selection or IDs incorrect.");
        }
        finally
        {
            layerDragPayload=null;SelectTab(source.Id);session.Load(before);
            foreach(var id in workspace.Documents.Select(t=>t.Id).Where(id=>!ids.Contains(id)).ToArray()){workspace.Close(id,true);tabViews.Remove(id);}Refresh();
        }
    }
}
