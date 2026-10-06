using Compositor.Core;
using Xunit;
namespace Compositor.Tests;
public sealed class LayerDropTests
{
    [Fact] public void DropAboveAnchorPreservesOrderOfMultipleRoots()
    {
        var d=Document.Create(1,1) with{Layers=[.."ABCDE".Select(c=>Layer.Blank(c.ToString(),1,1))]};
        var selection=new[]{d.Layers[3].Id,d.Layers[1].Id};
        var next=LayerHierarchy.PlaceSelected(d,selection,null,d.Layers[4].Id);
        Assert.Equal("ACEBD",string.Concat(next.Layers.Select(l=>l.Name)));
        var bottom=LayerHierarchy.PlaceSelected(d,selection,null,atBottom:true);
        Assert.Equal("BDACE",string.Concat(bottom.Layers.Select(l=>l.Name)));
        Assert.Same(d,LayerHierarchy.PlaceSelected(d,new[]{d.Layers[^1].Id},null,d.Layers[^2].Id));
    }
    [Fact] public void DropIntoFolderCarriesSubtreeWithoutDuplicatingChild()
    {
        var d=Document.Create(2,2);var a=d.Layers[0];var g=Layer.Group("G",2,2);d=LayerHierarchy.Wrap(d,a.Id,g);
        var target=Layer.Group("Target",2,2);var sibling=Layer.Blank("Sibling",2,2) with{ParentId=target.Id};d=d with{Layers=d.Layers.Add(target).Add(sibling)};
        var s=new EditorSession(d);s.SelectLayers(new[]{g.Id,a.Id},g.Id);
        s.Apply(doc=>LayerHierarchy.PlaceSelected(doc,s.SelectedLayerIds,target.Id));
        Assert.Equal(new[]{sibling.Id,g.Id},s.Document.Layers.Where(l=>l.ParentId==target.Id).Select(l=>l.Id));
        Assert.Same(d.Layers.Single(l=>l.Id==a.Id),s.Document.Layers.Single(l=>l.Id==a.Id));
        var next=s.Document;s.Undo();Assert.Same(d,s.Document);Assert.Equal(2,s.SelectedLayerIds.Count);s.Redo();Assert.Same(next,s.Document);
    }
    [Fact] public void InvalidDropCannotPartiallyEditDocument()
    {
        var d=Document.Create(2,2);var a=d.Layers[0];var g=Layer.Group("G",2,2);d=LayerHierarchy.Wrap(d,a.Id,g);
        var s=new EditorSession(d);s.SelectLayers(new[]{g.Id});
        Assert.Throws<InvalidOperationException>(()=>s.Apply(doc=>LayerHierarchy.PlaceSelected(doc,s.SelectedLayerIds,g.Id)));
        Assert.Throws<InvalidOperationException>(()=>LayerHierarchy.PlaceSelected(d,new[]{g.Id},null,a.Id));
        Assert.Throws<InvalidOperationException>(()=>LayerHierarchy.PlaceSelected(d,new[]{a.Id},null,Guid.NewGuid()));
        Assert.Throws<ArgumentException>(()=>LayerHierarchy.PlaceSelected(d,new[]{a.Id},null,g.Id,true));
        Assert.Same(d,s.Document);Assert.False(s.CanUndo);Assert.False(s.InTransaction);
    }
}