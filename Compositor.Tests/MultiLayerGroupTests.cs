using Compositor.Core;
using Xunit;
namespace Compositor.Tests;
public sealed class MultiLayerGroupTests
{
    [Fact] public void CrossBranchGroupingUsesClosestCommonParentAndHighestBranch()
    {
        var d=Document.Create(20,20);var a=d.Layers[0];var left=Layer.Group("Left",20,20);
        d=LayerHierarchy.Wrap(d,a.Id,left);var right=Layer.Group("Right",20,20);
        var b=Layer.Blank("B",20,20) with{ParentId=right.Id};var top=Layer.Blank("Top",20,20);
        var outer=Layer.Group("Outer",20,20);
        d=d with{Layers=d.Layers.Add(right).Add(b).Add(top).Add(outer)};
        d=d.Replace(left with{ParentId=outer.Id}).Replace(right with{ParentId=outer.Id}).Replace(top with{ParentId=outer.Id});
        var group=Layer.Group("Combined",20,20);var next=LayerHierarchy.WrapSelected(d,new[]{a.Id,b.Id},group);
        Assert.Equal(outer.Id,next.Layers.Single(l=>l.Id==group.Id).ParentId);
        Assert.Equal(new[]{left.Id,right.Id,group.Id,top.Id},next.Layers.Where(l=>l.ParentId==outer.Id).Select(l=>l.Id));
        Assert.Equal(new[]{a.Id,b.Id},next.Layers.Where(l=>l.ParentId==group.Id).Select(l=>l.Id));
        Assert.Same(a.Pixels,next.Layers.Single(l=>l.Id==a.Id).Pixels);Assert.Equal(left.Id,d.Layers.Single(l=>l.Id==a.Id).ParentId);
        next.Validate();
    }
    [Fact] public void SelectedFolderCarriesSelectedDescendantWithoutFlattening()
    {
        var d=Document.Create(10,10);var a=d.Layers[0];var folder=Layer.Group("Folder",10,10);
        d=LayerHierarchy.Wrap(d,a.Id,folder);var b=Layer.Blank("B",10,10);d=d with{Layers=d.Layers.Add(b)};
        var group=Layer.Group("Combined",10,10);var next=LayerHierarchy.WrapSelected(d,new[]{folder.Id,a.Id,b.Id},group);
        Assert.Equal(folder.Id,next.Layers.Single(l=>l.Id==a.Id).ParentId);
        Assert.Equal(new[]{folder.Id,b.Id},next.Layers.Where(l=>l.ParentId==group.Id).Select(l=>l.Id));
        Assert.Equal(4,next.Layers.Length);Assert.Same(d.Layers.Single(l=>l.Id==a.Id),next.Layers.Single(l=>l.Id==a.Id));
    }
    [Fact] public void NoncontiguousSiblingsRetainOrderAtHighestSelectedPosition()
    {
        var d=Document.Create(10,10);var a=d.Layers[0];var b=Layer.Blank("B",10,10);var c=Layer.Blank("C",10,10);var e=Layer.Blank("D",10,10);
        d=d with{Layers=d.Layers.Add(b).Add(c).Add(e)};var group=Layer.Group("Combined",10,10);
        var next=LayerHierarchy.WrapSelected(d,new[]{c.Id,a.Id},group);
        Assert.Equal(new[]{b.Id,group.Id,e.Id},next.Layers.Where(l=>l.ParentId is null).Select(l=>l.Id));
        Assert.Equal(new[]{a.Id,c.Id},next.Layers.Where(l=>l.ParentId==group.Id).Select(l=>l.Id));
    }
    [Fact] public void InvalidSelectionAndExcessDepthLeaveSourceUntouched()
    {
        var d=Document.Create(1,1);var image=d.Layers[0];
        Assert.Throws<InvalidOperationException>(()=>LayerHierarchy.WrapSelected(d,Array.Empty<Guid>(),Layer.Group("G",1,1)));
        for(int i=0;i<64;i++)d=LayerHierarchy.Wrap(d,d.Layers.First(l=>l.ParentId is null).Id,Layer.Group("G"+i,1,1));
        Assert.Throws<InvalidDataException>(()=>LayerHierarchy.WrapSelected(d,new[]{image.Id},Layer.Group("Too deep",1,1)));
        d.Validate();Assert.Equal(65,d.Layers.Length);
    }
}