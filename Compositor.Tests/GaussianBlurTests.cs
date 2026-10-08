using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class GaussianBlurTests
{
    private static Document Solid(int width,int height)
    {
        var bytes=new byte[width*height*4];for(int i=0;i<bytes.Length;i+=4){bytes[i]=255;bytes[i+3]=255;}
        var doc=Document.Create(400,100);return doc.Replace(doc.Layers[0] with{Pixels=Raster.FromRgba(width,height,bytes),Transform=new(10,10,width,height)});
    }
    private static byte Alpha(Layer layer,PointD point)
    {
        var p=layer.Transform.ToPixels(point,layer.Pixels.Width,layer.Pixels.Height);int x=(int)Math.Floor(p.X),y=(int)Math.Floor(p.Y);
        if(x<0||x>=layer.Pixels.Width||y<0||y>=layer.Pixels.Height)return 0;
        return layer.Pixels.Tiles.TryGetValue(new(x/256,y/256),out var tile)?tile.Bytes[((y%256)*256+x%256)*4+3]:(byte)0;
    }
    [Fact] public void SpreadsTransparentEdgesAcrossTilesAndPreservesPremultipliedColor()
    {
        var original=Solid(260,12);var source=original.Layers[0];var bytes=source.Pixels.ToRgba();
        var result=GaussianBlur.Apply(original,source.Id,2).Layers[0];
        Assert.True(result.Transform.X<source.Transform.X);Assert.True(result.Transform.Width>source.Transform.Width);
        Assert.InRange(Alpha(result,new(10.5,15.5)),(byte)1,(byte)254);Assert.True(Alpha(result,new(9.5,15.5))>0);
        Assert.True(Alpha(result,new(265.5,15.5))>0);
        foreach(var pixel in result.Pixels.ToRgba().Chunk(4)){Assert.Equal(pixel[3],pixel[0]);Assert.Equal(0,pixel[1]);Assert.Equal(0,pixel[2]);}
        Assert.Equal(bytes,source.Pixels.ToRgba());
    }
    [Fact] public void SelectionProtectsUnselectedPixelsAndRotatedMaskPlacementAndUndoRemainImmutable()
    {
        var original=Solid(12,12);var layer=original.Layers[0] with{Mask=LayerMask.Solid(12,12)};
        original=original.Replace(layer) with{Selection=SelectionGeometry.Box(10,10,2,12)};
        var next=GaussianBlur.Apply(original,layer.Id,2);var result=next.Layers[0];
        Assert.Equal(255,Alpha(result,new(21.5,15.5)));Assert.InRange(Alpha(result,new(10.5,15.5)),(byte)1,(byte)254);
        Assert.Same(layer.Mask!.Pixels,result.Mask!.Pixels);Assert.Equal(layer.Transform,result.Mask.Placement);Assert.Null(layer.Mask.Placement);
        var session=new EditorSession(original);session.Apply(_=>next);session.Undo();Assert.Same(original,session.Document);session.Redo();Assert.Same(next,session.Document);
        var rotated=original.Replace(layer with{Transform=layer.Transform with{Rotation=30,FlipX=true}}) with{Selection=null};
        var center=rotated.Layers[0].Transform.ToDocument(new(6,6),12,12);var blurred=GaussianBlur.Apply(rotated,layer.Id,2).Layers[0];
        var actual=blurred.Transform.ToDocument(new(blurred.Pixels.Width/2d,blurred.Pixels.Height/2d),blurred.Pixels.Width,blurred.Pixels.Height);
        Assert.Equal(center.X,actual.X,6);Assert.Equal(center.Y,actual.Y,6);Assert.True(blurred.Transform.FlipX);Assert.Equal(30,blurred.Transform.Rotation);
    }
    [Fact] public void InvalidOrCancelledWorkCannotChangeSourceAndBudgetIsCheckedBeforeGrowth()
    {
        var doc=Solid(12,12);var layer=doc.Layers[0];
        Assert.Throws<ArgumentOutOfRangeException>(()=>GaussianBlur.Apply(doc,layer.Id,double.NaN));
        using var token=new CancellationTokenSource();token.Cancel();Assert.Throws<OperationCanceledException>(()=>GaussianBlur.Apply(doc,layer.Id,2,cancellation:token.Token));
        var oversized=doc.Replace(layer with{Pixels=new Raster(30000,1),Transform=new(0,0,30000,1)});
        Assert.Same(oversized,GaussianBlur.Apply(oversized,layer.Id,2));
        var full=doc.Replace(layer with{Pixels=RasterReframe.Apply(layer.Pixels,new(0,0,30000,12)),Transform=new(0,0,30000,12)});
        Assert.Throws<InvalidDataException>(()=>GaussianBlur.Apply(full,layer.Id,2));
    }
    [Fact] public void SelectionOutsideLayerDoesNotChangeMetadataOrCreateUndo()
    {
        var doc=Solid(12,12) with{Selection=SelectionGeometry.Box(200,50,10,10)};
        Assert.Same(doc,GaussianBlur.Apply(doc,doc.Layers[0].Id,2));
    }
    [Fact] public void PaddingBudgetUsesTheSameEmptyLayerRuleAsDocumentValidation()
    {
        var doc=Solid(12,12);
        for(int i=0;i<8;i++)doc=doc with{Layers=doc.Layers.Add(Layer.Blank("Empty "+i,4000,4000))};
        doc.Validate();var next=GaussianBlur.Apply(doc,doc.Layers[0].Id,2);
        Assert.NotSame(doc,next);
        for(int i=1;i<doc.Layers.Length;i++)Assert.Same(doc.Layers[i],next.Layers[i]);
    }
    [Fact] public void LargePreviewHasBoundedGridOriginalPlacementAndContinuousTileSampling()
    {
        var doc=Solid(2600,40);var layer=doc.Layers[0] with{Mask=LayerMask.Solid(2600,40)};doc=doc.Replace(layer);
        var before=layer.Pixels.ToRgba();var preview=GaussianBlur.Preview(doc,layer.Id,4).Layers[0];
        Assert.Equal(2048,preview.Pixels.Width);Assert.True(preview.Pixels.Height<2048);
        Assert.Equal(-4,preview.Transform.X,6);Assert.Equal(-4,preview.Transform.Y,6);
        Assert.Equal(2628,preview.Transform.Width);Assert.Equal(68,preview.Transform.Height);
        Assert.Same(layer.Mask!.Pixels,preview.Mask!.Pixels);Assert.Equal(layer.Transform,preview.Mask.Placement);
        Assert.InRange(Alpha(preview,new(265.5,30.5)),(byte)250,(byte)255);
        Assert.InRange(Alpha(preview,new(266.5,30.5)),(byte)250,(byte)255);
        foreach(var pixel in preview.Pixels.ToRgba().Chunk(4))Assert.Equal(pixel[3],pixel[0]);
        Assert.Equal(before,layer.Pixels.ToRgba());
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>GaussianBlur.Preview(doc,layer.Id,4,cancellation:cancellation.Token));
    }
}
