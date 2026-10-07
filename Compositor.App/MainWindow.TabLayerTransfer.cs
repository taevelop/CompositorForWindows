using System.Windows;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private bool CanTransferTabLayers(Guid? target,IDataObject data,bool alt)
    {
        return !alt&&!busy&&!session.InTransaction&&target!=workspace.Current.Id&&
            (target is null||workspace.Documents.Any(t=>t.Id==target&&!t.Session.InTransaction))&&
            layerDragPayload is {} payload&&ReferenceEquals(payload.Source,session.Document)&&
            ReferenceEquals(data.GetData(LayerDragFormat),payload);
    }
    private void TransferTabLayers(Guid? target,IDataObject data,bool alt)
    {
        if(!CanTransferTabLayers(target,data,alt))return;
        var payload=layerDragPayload!;var source=workspace.Current;
        Guid anchor=session.ActiveLayerId is {} active&&payload.Ids.Contains(active)?active:payload.Ids[^1];
        var targetTab=target is {} id?workspace.Documents.Single(t=>t.Id==id):null;
        var destination=targetTab?.Session.Document??(Document.Create(payload.Source.Width,payload.Source.Height) with{Layers=[]});
        // Validate the whole transfer before creating a tab or changing selection.
        var copy=DocumentLayerTransfer.Create(payload.Source,destination,payload.Ids,anchor);
        var folded=collapsedGroups.ToHashSet();
        if(targetTab is null)AddDocumentTab(destination);else SelectTab(targetTab.Id);
        if(workspace.Current==source)return;
        session.Apply(_=>copy.Document);session.SelectLayers(copy.Roots,copy.Roots[^1]);
        foreach(var old in folded)if(copy.Mapping.TryGetValue(old,out var mapped))collapsedGroups.Add(mapped);
        Refresh();
    }
}
