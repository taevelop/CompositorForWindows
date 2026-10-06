using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class MultiLayerCopyTests
{
    [Fact] public void SelectedRootsDuplicateOnceInOrderAndHistoryRestoresSelection()
    {
        var d=Document.Create(10,10);var image=d.Layers[0] with{Mask=LayerMask.Solid(1,1,80),Effects=new(ColorOverlay:new(.2,.3,.4))};
        d=d.Replace(image);var group=Layer.Group("Group",10,10);d=LayerHierarchy.Wrap(d,image.Id,group);
        var other=Layer.Blank("Other",5,5) with{Visible=false};d=d with{Layers=d.Layers.Add(other)};
        var s=new EditorSession(d);s.SelectLayers(new[]{group.Id,image.Id,other.Id},image.Id);
        var map=LayerCopy.DuplicateSelected(s);Assert.Equal(3,map.Count);Assert.Equal(6,s.Document.Layers.Length);
        Assert.Equal(new[]{group.Id,map[group.Id],other.Id,map[other.Id]},s.Document.Layers.Where(l=>l.ParentId is null).Select(l=>l.Id));
        var copy=s.Document.Layers.First(l=>l.Id==map[image.Id]);Assert.Equal(map[group.Id],copy.ParentId);
        Assert.Equal(image.Name,copy.Name);Assert.Same(image.Pixels,copy.Pixels);Assert.Same(image.Mask,copy.Mask);Assert.Same(image.Effects,copy.Effects);
        Assert.Equal(2,s.SelectedLayerIds.Count);Assert.Contains(map[group.Id],s.SelectedLayerIds);Assert.Equal(map[other.Id],s.ActiveLayerId);
        Assert.False(s.ActiveLayer!.Visible);Assert.Equal(1,s.UndoCount);Assert.Equal(0,s.HistoryRetainedBytes);
        var next=s.Document;s.Undo();Assert.Same(d,s.Document);Assert.Equal(3,s.SelectedLayerIds.Count);Assert.Equal(image.Id,s.ActiveLayerId);
        s.Redo();Assert.Same(next,s.Document);Assert.Equal(2,s.SelectedLayerIds.Count);
    }
    [Fact] public void ChildrenWithDifferentUnselectedParentsKeepTheirParents()
    {
        var d=Document.Create(10,10);var a=d.Layers[0];var g=Layer.Group("A",10,10);d=LayerHierarchy.Wrap(d,a.Id,g);
        var h=Layer.Group("B",10,10);var b=Layer.Blank("B",10,10) with{ParentId=h.Id};d=d with{Layers=d.Layers.Add(h).Add(b)};
        var s=new EditorSession(d);s.SelectLayers(new[]{a.Id,b.Id});var map=LayerCopy.DuplicateSelected(s);
        Assert.Equal(2,map.Count);Assert.Equal(g.Id,s.Document.Layers.First(l=>l.Id==map[a.Id]).ParentId);Assert.Equal(h.Id,s.Document.Layers.First(l=>l.Id==map[b.Id]).ParentId);
        s.Document.Validate();
    }
    [Fact] public void EmptyAndOverBudgetSelectionsCannotPartiallyDuplicate()
    {
        var d=Document.Create(10,10);var s=new EditorSession(d);s.SelectLayers(Array.Empty<Guid>());
        Assert.Empty(LayerCopy.DuplicateSelected(s));Assert.Same(d,s.Document);Assert.False(s.CanUndo);
        var huge=Layer.Blank("Huge",8000,8000) with{Mask=LayerMask.Solid(1,1,255)};s.Load(d with{Layers=[huge]});var before=s.Document;
        Assert.Throws<InvalidDataException>(()=>LayerCopy.DuplicateSelected(s));Assert.Same(before,s.Document);Assert.Single(s.SelectedLayerIds);Assert.False(s.InTransaction);Assert.False(s.CanUndo);
    }
}