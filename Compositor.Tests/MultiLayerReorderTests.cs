using Compositor.Core;
using Xunit;
namespace Compositor.Tests;
public sealed class MultiLayerReorderTests
{
    [Theory]
    [InlineData(1,"AB","CABD")]
    [InlineData(-1,"BC","BCAD")]
    [InlineData(1,"BD","ACBD")]
    [InlineData(-1,"AC","ACBD")]
    [InlineData(1,"CD","ABCD")]
    [InlineData(-1,"AB","ABCD")]
    public void SiblingStepsPreserveSelectedOrderAtBoundaries(int direction,string selected,string expected)
    {
        var d=Document.Create(1,1) with{Layers=[.."ABCD".Select(c=>Layer.Blank(c.ToString(),1,1))]};
        var next=LayerHierarchy.ReorderSelected(d,d.Layers.Where(l=>selected.Contains(l.Name)).Select(l=>l.Id),direction);
        Assert.Equal(expected,string.Concat(next.Layers.Select(l=>l.Name)));
        if(expected=="ABCD")Assert.Same(d,next);
        foreach(var l in next.Layers)Assert.Same(d.Layers.First(old=>old.Id==l.Id),l);
    }
    [Fact] public void SelectedParentCarriesChildWithoutReorderingItsChildren()
    {
        var d=Document.Create(1,1);var a=d.Layers[0];var group=Layer.Group("Group",1,1);d=LayerHierarchy.Wrap(d,a.Id,group);
        var child=Layer.Blank("Child",1,1) with{ParentId=group.Id};var other=Layer.Blank("Other",1,1);
        d=d with{Layers=d.Layers.Add(child).Add(other)};var s=new EditorSession(d);s.SelectLayers(new[]{group.Id,a.Id},a.Id);
        s.Apply(doc=>LayerHierarchy.ReorderSelected(doc,s.SelectedLayerIds,1));
        Assert.Equal(new[]{other.Id,group.Id},s.Document.Layers.Where(l=>l.ParentId is null).Select(l=>l.Id));
        Assert.Equal(new[]{a.Id,child.Id},s.Document.Layers.Where(l=>l.ParentId==group.Id).Select(l=>l.Id));
        Assert.Equal(1,s.UndoCount);var next=s.Document;s.Undo();Assert.Same(d,s.Document);Assert.Equal(2,s.SelectedLayerIds.Count);s.Redo();Assert.Same(next,s.Document);
    }
    [Fact] public void SeparateParentsReorderIndependentlyAndNoOpKeepsHistoryEmpty()
    {
        var g=Layer.Group("G",1,1);var h=Layer.Group("H",1,1);
        var a=Layer.Blank("A",1,1) with{ParentId=g.Id};var b=Layer.Blank("B",1,1) with{ParentId=g.Id};
        var c=Layer.Blank("C",1,1) with{ParentId=h.Id};var e=Layer.Blank("D",1,1) with{ParentId=h.Id};
        var d=Document.Create(1,1) with{Layers=[g,a,b,h,c,e]};var s=new EditorSession(d);s.SelectLayers(new[]{a.Id,c.Id});
        s.Apply(doc=>LayerHierarchy.ReorderSelected(doc,s.SelectedLayerIds,-1));Assert.False(s.CanUndo);
        s.Apply(doc=>LayerHierarchy.ReorderSelected(doc,s.SelectedLayerIds,1));
        Assert.Equal(new[]{b.Id,a.Id},s.Document.Layers.Where(l=>l.ParentId==g.Id).Select(l=>l.Id));
        Assert.Equal(new[]{e.Id,c.Id},s.Document.Layers.Where(l=>l.ParentId==h.Id).Select(l=>l.Id));
        Assert.Throws<ArgumentOutOfRangeException>(()=>LayerHierarchy.ReorderSelected(d,new[]{a.Id},2));
    }
}