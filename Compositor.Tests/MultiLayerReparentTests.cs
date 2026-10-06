using Compositor.Core;
using Xunit;
namespace Compositor.Tests;
public sealed class MultiLayerReparentTests
{
    [Fact] public void MoveRootsTogetherWithoutPullingSelectedChildOut()
    {
        var d=Document.Create(10,10);var child=d.Layers[0];var folder=Layer.Group("Source",10,10);d=LayerHierarchy.Wrap(d,child.Id,folder);
        var other=Layer.Blank("Other",10,10);var target=Layer.Group("Target",10,10);d=d with{Layers=d.Layers.Add(other).Add(target)};
        var s=new EditorSession(d);s.SelectLayers(new[]{folder.Id,child.Id,other.Id},child.Id);
        s.Apply(doc=>LayerHierarchy.ReparentSelected(doc,s.SelectedLayerIds,target.Id));
        Assert.Equal(new[]{folder.Id,other.Id},s.Document.Layers.Where(l=>l.ParentId==target.Id).Select(l=>l.Id));
        Assert.Same(d.Layers.Single(l=>l.Id==child.Id),s.Document.Layers.Single(l=>l.Id==child.Id));
        Assert.Equal(3,s.SelectedLayerIds.Count);Assert.Equal(child.Id,s.ActiveLayerId);var next=s.Document;
        s.Undo();Assert.Same(d,s.Document);Assert.Equal(3,s.SelectedLayerIds.Count);s.Redo();Assert.Same(next,s.Document);
    }
    [Fact] public void MovingOutPlacesEachSiblingSetAboveItsOldParent()
    {
        var d=Document.Create(10,10);var a=d.Layers[0];var g=Layer.Group("G",10,10);d=LayerHierarchy.Wrap(d,a.Id,g);
        var b=Layer.Blank("B",10,10) with{ParentId=g.Id};var h=Layer.Group("H",10,10);var c=Layer.Blank("C",10,10) with{ParentId=h.Id};
        var top=Layer.Blank("Top",10,10);d=d with{Layers=d.Layers.Add(b).Add(h).Add(c).Add(top)};
        var next=LayerHierarchy.MoveSelectedOut(d,new[]{b.Id,c.Id,a.Id,top.Id});
        Assert.Equal(new[]{g.Id,a.Id,b.Id,h.Id,c.Id,top.Id},next.Layers.Select(l=>l.Id));
        Assert.All(next.Layers,l=>Assert.Null(l.ParentId));Assert.Same(top,next.Layers[^1]);
    }
    [Fact] public void InvalidDestinationCancelsWholeEditAndNoOpAddsNoUndo()
    {
        var d=Document.Create(10,10);var a=d.Layers[0];var g=Layer.Group("G",10,10);d=LayerHierarchy.Wrap(d,a.Id,g);
        var b=Layer.Blank("B",10,10);d=d with{Layers=d.Layers.Add(b)};var s=new EditorSession(d);s.SelectLayers(new[]{g.Id,b.Id});
        Assert.Throws<InvalidOperationException>(()=>s.Apply(doc=>LayerHierarchy.ReparentSelected(doc,s.SelectedLayerIds,g.Id)));
        Assert.Same(d,s.Document);Assert.False(s.CanUndo);Assert.False(s.InTransaction);Assert.Equal(2,s.SelectedLayerIds.Count);
        s.Apply(doc=>LayerHierarchy.ReparentSelected(doc,s.SelectedLayerIds,null));Assert.Same(d,s.Document);Assert.False(s.CanUndo);
        Assert.Throws<InvalidOperationException>(()=>LayerHierarchy.ReparentSelected(d,new[]{b.Id},Guid.NewGuid()));
    }
    [Fact] public void DestinationDepthIsCheckedBeforeAnyChange()
    {
        var d=Document.Create(1,1);var a=d.Layers[0];Guid? deepest=null;
        for(int i=0;i<64;i++){var g=Layer.Group("G"+i,1,1);deepest??=g.Id;d=LayerHierarchy.Wrap(d,d.Layers.First(l=>l.ParentId is null).Id,g);}
        var moving=Layer.Group("Moving",1,1);d=d with{Layers=d.Layers.Add(moving)};
        Assert.Throws<InvalidDataException>(()=>LayerHierarchy.ReparentSelected(d,new[]{moving.Id},deepest));d.Validate();
    }
}