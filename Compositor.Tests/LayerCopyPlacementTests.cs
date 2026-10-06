using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class LayerCopyPlacementTests
{
    [Fact] public void CopyAboveOwnSourceKeepsOriginalsAndRelativeOrder()
    {
        var d=Document.Create(2,2);var a=d.Layers[0];var b=Layer.Blank("B",2,2);d=d with{Layers=d.Layers.Add(b)};
        var plan=LayerCopyPlacement.Create(d,new[]{b.Id,a.Id},null,a.Id);
        Assert.Equal(new[]{a.Id,plan.Mapping[a.Id],plan.Mapping[b.Id],b.Id},plan.Document.Layers.Select(l=>l.Id));
        Assert.Equal(new[]{plan.Mapping[a.Id],plan.Mapping[b.Id]},plan.Roots);
        Assert.Same(a,plan.Document.Layers[0]);Assert.Same(a.Pixels,plan.Document.Layers[1].Pixels);Assert.Equal(2,d.Layers.Length);
    }
    [Fact] public void CopyFolderMapsChildrenOnceAndPreservesEffectsAndMasks()
    {
        var d=Document.Create(4,4);var image=d.Layers[0] with{Mask=LayerMask.Solid(1,1,100),Effects=new(ColorOverlay:new(.2,.3,.4))};d=d.Replace(image);
        var g=Layer.Group("G",4,4);d=LayerHierarchy.Wrap(d,image.Id,g);var target=Layer.Group("Target",4,4);d=d with{Layers=d.Layers.Add(target)};
        var plan=LayerCopyPlacement.Create(d,new[]{g.Id,image.Id},target.Id);
        Assert.Equal(2,plan.Mapping.Count);Assert.Single(plan.Roots);
        var copied=plan.Document.Layers.Single(l=>l.Id==plan.Mapping[image.Id]);
        Assert.Equal(plan.Mapping[g.Id],copied.ParentId);Assert.Equal(target.Id,plan.Document.Layers.Single(l=>l.Id==plan.Roots[0]).ParentId);
        Assert.Same(image.Pixels,copied.Pixels);Assert.Same(image.Mask,copied.Mask);Assert.Same(image.Effects,copied.Effects);
        Assert.Throws<InvalidOperationException>(()=>LayerCopyPlacement.Create(d,new[]{g.Id},g.Id));
    }
    [Fact] public void InvalidTargetOrBudgetCannotMutateSource()
    {
        var d=Document.Create(1,1);var image=Layer.Blank("Huge",8000,8000) with{Mask=LayerMask.Solid(1,1,255)};d=d with{Layers=[image]};
        Assert.Throws<InvalidDataException>(()=>LayerCopyPlacement.Create(d,new[]{image.Id},null));
        Assert.Single(d.Layers);Assert.Same(image,d.Layers[0]);
        var small=Document.Create(2,2);
        Assert.Throws<InvalidOperationException>(()=>LayerCopyPlacement.Create(small,new[]{small.Layers[0].Id},null,Guid.NewGuid()));
    }
}