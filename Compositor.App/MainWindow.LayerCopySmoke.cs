using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private void LayerCopySmokeTest()
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        var d=Document.Create(12,12);d=d.Replace(d.Layers[0] with{Pixels=LayerMask.Solid(12,12,90).Pixels});
        session.Load(d);LayerViaCopy(null,new());
        Check(session.Document.Layers.Length==2&&ReferenceEquals(d.Layers[0].Pixels,session.ActiveLayer!.Pixels),"Duplicate handler failed.");
        session.Undo();Check(ReferenceEquals(d,session.Document),"Duplicate handler Undo failed.");
        session.Load(d with{Selection=SelectionGeometry.Box(2,2,4,5,false,false)});var selected=session.Document;
        LayerViaCopy(null,new());Check(session.ActiveLayer!.Pixels.Width==4&&session.ActiveLayer.Pixels.Height==5&&session.Document.Selection is null,"Layer via Copy handler failed.");
        session.Undo();Check(ReferenceEquals(selected,session.Document),"Layer via Copy Undo failed.");
        var group=Layer.Group("Group",12,12);var grouped=LayerHierarchy.Wrap(d,d.Layers[0].Id,group);
        session.Load(grouped,group.Id);collapsedGroups.Add(group.Id);DuplicateLayer(null,new());
        Check(session.ActiveLayer!.IsGroup&&LayerHierarchy.Subtree(session.Document,session.ActiveLayerId!.Value).Count==2,"Group duplicate handler failed.");
        Check(collapsedGroups.Contains(session.ActiveLayerId!.Value),"Group copy lost collapsed state.");
        session.Undo();Check(ReferenceEquals(grouped,session.Document)&&session.ActiveLayerId==group.Id,"Group duplication Undo failed.");
        var other=Layer.Blank("Other",12,12);var multiple=grouped with{Layers=grouped.Layers.Add(other)};
        session.Load(multiple,group.Id);collapsedGroups.Add(group.Id);Refresh();
        Layers.SelectedItems.Add(Layers.Items.Cast<LayerRow>().Single(r=>r.Id==other.Id));
        DuplicateLayer(null,new());
        Check(session.Document.Layers.Length==6&&session.SelectedLayerIds.Count==2&&Layers.SelectedItems.Count==2,"Multiple duplicate panel selection failed.");
        var copiedGroup=session.Document.Layers.Single(l=>l.IsGroup&&l.Id!=group.Id);
        Check(collapsedGroups.Contains(copiedGroup.Id),"Multiple duplicate lost collapsed group state.");
        session.Undo();Check(ReferenceEquals(multiple,session.Document)&&session.SelectedLayerIds.SetEquals(new[]{group.Id,other.Id}),"Multiple duplicate Undo selection failed.");
    }
}
