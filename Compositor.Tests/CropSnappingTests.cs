using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class CropSnappingTests
{
    [Fact] public void MoveSnapsClosestEdgeWithoutChangingSizeEvenWithRatio()
    {
        var f=new CropFrame(3,4,20,10);var drag=new CropDrag(f,new(0,0),CropDragMode.Move);
        Assert.Equal(new CropFrame(5,5,20,10),CropSnapping.Apply(f,drag,new(0,0),2,false,[0,25],[0,15],6));
    }
    [Fact] public void RatioCreationDoesNotSnapAndSideHandleOnlyChangesItsAxis()
    {
        var f=new CropFrame(3,4,20,10);
        Assert.Equal(f,CropSnapping.Apply(f,new(f,new(3,4),CropDragMode.Create),new(23,14),2,false,[25],[15],6));
        Assert.Equal(new CropFrame(3,4,22,10),CropSnapping.Apply(f,new(f,new(23,9),CropDragMode.Resize,3),new(23,9),null,false,[25],[15],6));
    }
    [Fact] public void CenteredCreationMirrorsSnappedEdgesAndRejectsCrossingTargets()
    {
        var f=new CropFrame(5,5,10,10);var drag=new CropDrag(f,new(10,10),CropDragMode.Create);
        Assert.Equal(new CropFrame(4,4,12,12),CropSnapping.Apply(f,drag,new(15,15),null,true,[16],[16],2));
        var thin=new CropFrame(10,0,1,10);
        Assert.Equal(thin,CropSnapping.Apply(thin,new(thin,new(10,0),CropDragMode.Resize,7),new(10,5),null,false,[11],[],2));
    }
}
