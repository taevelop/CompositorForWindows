using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;

public class LayerMaskCopyTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CopyPinsDocumentPlacementSharesPixelsAndPreservesStateAndUndo(bool independent)
    {
        var doc=Document.Create(80,80);var source=doc.Layers[0] with{Mask=LayerMask.Solid(4,4,96) with{Enabled=false,Linked=!independent,Placement=independent?new(20,25,10,12,Rotation:15):null},Pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,.4,.5,.6),4,4),Transform=new(10,10,4,4)};
        var target=source with{Id=Guid.NewGuid(),Transform=new(40,40,20,20),Mask=LayerMask.Solid(4,4)};doc=doc.Replace(source) with{Layers=doc.Replace(source).Layers.Add(target)};
        var after=LayerMaskCopy.Apply(doc,source.Id,target.Id);var copy=after.Layers[1].Mask!;
        Assert.Same(source.Mask!.Pixels,copy.Pixels);Assert.Equal(source.Mask.Placement??source.Transform,copy.Placement);Assert.Equal(source.Mask.Linked,copy.Linked);Assert.False(copy.Enabled);
        Assert.Same(target.Pixels,after.Layers[1].Pixels);Assert.Same(source.Mask,doc.Layers[0].Mask);
        var session=new EditorSession(doc);session.Begin();session.Preview(after);session.Commit();session.Undo();Assert.Same(doc,session.Document);session.Redo();Assert.Same(after,session.Document);
        Assert.Same(doc,LayerMaskCopy.Apply(doc,source.Id,source.Id));
    }
    [Fact]
    public void GroupTargetAndMissingMaskAreRejected()
    {
        var doc=Document.Create(8,8);var source=doc.Layers[0] with{Mask=LayerMask.Solid(8,8)};var group=source with{Id=Guid.NewGuid(),IsGroup=true,Mask=null,Pixels=new Raster(1,1),Transform=new(0,0,1,1)};doc=doc.Replace(source) with{Layers=doc.Replace(source).Layers.Add(group)};
        Assert.Throws<InvalidOperationException>(()=>LayerMaskCopy.Apply(doc,source.Id,group.Id));
        Assert.Throws<InvalidOperationException>(()=>LayerMaskCopy.Apply(doc,group.Id,source.Id));
    }
}
