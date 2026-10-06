using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public sealed class SelectedPixelTransferTests
{
    private static Layer Solid(int width,int height,byte red=80,byte alpha=128)
    {
        var bytes=new byte[width*height*4];
        for(int p=0;p<bytes.Length;p+=4){bytes[p]=red;bytes[p+1]=20;bytes[p+2]=10;bytes[p+3]=alpha;}
        return Layer.Blank("Source",width,height) with{Pixels=Raster.FromRgba(width,height,bytes)};
    }
    [Fact] public void LiftAndCutPreservePremultiplicationAndOriginal()
    {
        var layer=Solid(8,4);var before=layer.Pixels.ToRgba();
        var coverage=SelectionCoverage.Create(SelectionGeometry.Box(1,0,2,4,false,false),8,4);
        var transfer=SelectedPixelTransfer.Lift(layer,coverage)!;
        Assert.Equal((1,0,3,4),(transfer.Left,transfer.Top,transfer.Right,transfer.Bottom));
        Assert.Equal(new byte[]{80,20,10,128},transfer.Pixels.ToRgba().AsSpan(4,4).ToArray());
        Assert.Equal(0,transfer.CutSource.ToRgba()[7]);Assert.Equal(128,transfer.CutSource.ToRgba()[3]);
        Assert.Equal(before,layer.Pixels.ToRgba());
    }
    [Fact] public void EmptyAndTransparentSelectionsCannotBeLifted()
    {
        var layer=Layer.Blank("Empty",8,8);
        Assert.Null(SelectedPixelTransfer.Lift(layer,SelectionCoverage.Create(SelectionGeometry.Box(0,0,8,8),8,8)));
        Assert.Null(SelectedPixelTransfer.Lift(Solid(8,8),SelectionCoverage.Create(DocumentSelection.Empty,8,8)));
    }
    [Fact] public void IntegerPlacementAndDuplicateUseSourceOver()
    {
        var layer=Solid(8,1);
        var transfer=SelectedPixelTransfer.Lift(layer,SelectionCoverage.Create(SelectionGeometry.Box(1,0,1,1,false,false),8,1))!;
        var moved=transfer.CompositeOnto(transfer.CutSource,3,0).ToRgba();
        Assert.Equal(0,moved[7]);Assert.Equal(192,moved[4*4+3]);Assert.Equal(120,moved[4*4]);
        var duplicate=transfer.CompositeOnto(layer.Pixels,3,0).ToRgba();Assert.Equal(128,duplicate[7]);Assert.Equal(192,duplicate[19]);
    }
    [Theory][InlineData(Sampling.Smooth)][InlineData(Sampling.High)]
    public void FractionalSamplingCrossesTileBoundaryWithoutSeam(Sampling sampling)
    {
        var layer=Solid(520,4,100,255);
        var transfer=SelectedPixelTransfer.Lift(layer,SelectionCoverage.Create(SelectionGeometry.Box(200,0,110,4,false,false),520,4))!;
        var result=transfer.CompositeOnto(new Raster(520,4),.5,0,sampling).ToRgba();
        Assert.Equal(result[(2*520+255)*4],result[(2*520+256)*4]);
        Assert.Equal(255,result[(2*520+255)*4+3]);
        Assert.InRange(result[(2*520+200)*4+3],(byte)120,(byte)135);
        for(int p=0;p<result.Length;p+=4)for(int c=0;c<3;c++)Assert.True(result[p+c]<=result[p+3]);
    }
    [Fact] public void RepeatedPreviewsDoNotAccumulateAndUnchangedTilesAreShared()
    {
        var layer=Solid(800,4);
        var transfer=SelectedPixelTransfer.Lift(layer,SelectionCoverage.Create(SelectionGeometry.Feather(SelectionGeometry.Box(300,0,10,4),2),800,4))!;
        var first=transfer.CompositeOnto(transfer.CutSource,20,0);var snapshot=first.ToRgba();
        _=transfer.CompositeOnto(transfer.CutSource,100,0);
        var again=transfer.CompositeOnto(transfer.CutSource,20,0);
        Assert.Equal(snapshot,again.ToRgba());Assert.Equal(snapshot,first.ToRgba());
        Assert.Same(layer.Pixels.Tiles[new(0,0)],again.Tiles[new(0,0)]);
        Assert.Same(layer.Pixels.Tiles[new(2,0)],again.Tiles[new(2,0)]);
        Assert.Same(transfer.CutSource,transfer.CompositeOnto(transfer.CutSource,10000,0));
    }
    [Fact] public void RotatedLayerLiftUsesDocumentSelection()
    {
        var layer=Solid(4,4) with{Transform=new(10,10,8,8,90,true)};
        var point=layer.Transform.ToDocument(new(1.5,2.5),4,4);
        var selection=SelectionCoverage.Create(SelectionGeometry.Box(point.X-1,point.Y-1,2,2,false,false),30,30);
        var transfer=SelectedPixelTransfer.Lift(layer,selection)!;
        Assert.Equal(128,transfer.Pixels.ToRgba()[(2*4+1)*4+3]);
        Assert.Equal(0,transfer.Pixels.ToRgba()[3]);
    }
    [Fact] public void InvalidAndCancelledPlacementDoesNotChangeBase()
    {
        var layer=Solid(4,4);var transfer=SelectedPixelTransfer.Lift(layer,SelectionCoverage.Create(SelectionGeometry.Box(0,0,4,4),4,4))!;
        Assert.Throws<ArgumentOutOfRangeException>(()=>transfer.CompositeOnto(layer.Pixels,double.NaN,0));
        Assert.Throws<OperationCanceledException>(()=>transfer.CompositeOnto(layer.Pixels,1,0,cancellation:new CancellationToken(true)));
    }
    [Fact] public void LiftReframeAndMoveOutsideOldLayerKeepDocumentCoordinatesAndMask()
    {
        var layer=Solid(8,4) with{Transform=new(10,10,8,4,90),Mask=LayerMask.Solid(8,4,60)};
        var origin=layer.Transform.ToDocument(new(1.5,1.5),8,4);
        var coverage=SelectionCoverage.Create(SelectionGeometry.Box(origin.X-.5,origin.Y-.5,1,1,false,false),30,30);
        var transfer=SelectedPixelTransfer.Lift(layer,coverage)!;
        var zero=layer.Transform.ToPixels(new(0,0),8,4);
        var moved=layer.Transform.ToPixels(new(2,0),8,4);
        var frame=new RasterFrame(0,-4,8,8);
        var expanded=RasterReframe.Apply(layer with{Pixels=transfer.CutSource},frame);
        var pixels=transfer.CompositeOnto(expanded.Pixels,moved.X-zero.X-frame.Left,moved.Y-zero.Y-frame.Top);
        Assert.Equal(128,pixels.ToRgba()[(3*8+1)*4+3]);
        Assert.Equal(0,pixels.ToRgba()[(5*8+1)*4+3]);
        var target=expanded.Transform.ToDocument(new(1.5,3.5),8,8);
        Assert.Equal(origin.X+2,target.X,8);Assert.Equal(origin.Y,target.Y,8);
        Assert.Equal(255,expanded.Mask!.Pixels.ToRgba()[(3*8+1)*4]);
        Assert.Equal(60,expanded.Mask.Pixels.ToRgba()[(5*8+1)*4]);
    }}
