using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;
namespace Compositor.Tests;
public sealed class MotionBlurTests
{
    private static byte Alpha(Layer layer,int x,int y)
    {
        var point=layer.Transform.ToPixels(new(x+.5,y+.5),layer.Pixels.Width,layer.Pixels.Height);int px=(int)Math.Floor(point.X),py=(int)Math.Floor(point.Y);
        return px<0||py<0||px>=layer.Pixels.Width||py>=layer.Pixels.Height?(byte)0:layer.Pixels.Tiles.TryGetValue(new(px/256,py/256),out var tile)?tile.Bytes[((py%256)*256+px%256)*4+3]:(byte)0;
    }
    [Theory][InlineData(0,24,20,20,24)][InlineData(90,20,24,24,20)][InlineData(-90,20,24,24,20)][InlineData(45,23,17,17,17)][InlineData(-45,17,17,23,17)]
    public void MatchesOriginalCounterclockwiseDirectionAndPremultipliedStreak(double angle,int x,int y,int excludedX,int excludedY)
    {
        var bytes=new byte[41*41*4];bytes.AsSpan((20*41+20)*4,4).Fill(255);
        var doc=Document.Create(41,41);var layer=doc.Layers[0] with{Pixels=Raster.FromRgba(41,41,bytes)};doc=doc.Replace(layer);
        var result=MotionBlur.Apply(doc,layer.Id,16,angle).Layers[0];
        Assert.True(Alpha(result,x,y)>0);Assert.Equal(0,Alpha(result,excludedX,excludedY));
        foreach(var pixel in result.Pixels.ToRgba().Chunk(4)){Assert.Equal(pixel[3],pixel[0]);Assert.Equal(pixel[3],pixel[1]);Assert.Equal(pixel[3],pixel[2]);}
        Assert.Equal(bytes,layer.Pixels.ToRgba());
    }
    [Fact] public void StripHalosMatchWholeRotatedReferenceAcrossBoundaries()
    {
        const int size=320;const double angle=37,distance=24;
        using var input=new SKBitmap(CanvasRenderer.Info(size,size));using(var canvas=new SKCanvas(input)){canvas.Clear(SKColors.Transparent);using var paint=new SKPaint{Color=SKColors.White};canvas.DrawRect(20,20,280,280,paint);}
        var actual=MotionBlurRaster.Apply(input,distance,angle,default).ToRgba();
        double radians=angle*Math.PI/180;int width=(int)Math.Ceiling(size*Math.Abs(Math.Cos(radians))+size*Math.Abs(Math.Sin(radians)))+4;
        using var rotated=new SKBitmap(CanvasRenderer.Info(width,width));var sampling=new SKSamplingOptions(SKFilterMode.Linear);
        using(var canvas=new SKCanvas(rotated)){canvas.Clear(SKColors.Transparent);canvas.Translate(width/2f,width/2f);canvas.RotateDegrees((float)angle);canvas.Translate(-size/2f,-size/2f);canvas.DrawBitmap(input,0,0,sampling);}
        using var blurred=new SKBitmap(rotated.Info);using var filter=SKImageFilter.CreateBlur((float)(distance/Math.Sqrt(12)),0,SKShaderTileMode.Decal);using var paintBlur=new SKPaint{ImageFilter=filter,BlendMode=SKBlendMode.Src};
        using(var canvas=new SKCanvas(blurred)){canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(rotated,0,0,new SKSamplingOptions(SKFilterMode.Nearest),paintBlur);}
        using var output=new SKBitmap(input.Info);using(var canvas=new SKCanvas(output)){canvas.Clear(SKColors.Transparent);canvas.Translate(size/2f,size/2f);canvas.RotateDegrees((float)-angle);canvas.Translate(-width/2f,-width/2f);canvas.DrawBitmap(blurred,0,0,sampling);}
        var expected=output.GetPixelSpan();int max=0;for(int i=0;i<actual.Length;i++)max=Math.Max(max,Math.Abs(actual[i]-expected[i]));
        Assert.InRange(max,0,2);
    }
    [Fact] public void SelectionMaskAndUndoUseOriginalCoordinatesAndRejectInvalidSettings()
    {
        var doc=Document.Create(40,40);var style=new LayerShapeStyle(ShapeKind.Rectangle,1,0,0);var layer=doc.Layers[0] with{Pixels=ShapeRaster.Create(style,12,12),Transform=new(10,10,12,12),Mask=LayerMask.Solid(12,12)};
        doc=doc.Replace(layer) with{Selection=SelectionGeometry.Box(10,10,2,12)};var next=MotionBlur.Apply(doc,layer.Id,16,0);var result=next.Layers[0];
        Assert.InRange(Alpha(result,10,15),(byte)1,(byte)254);Assert.Equal(255,Alpha(result,21,15));
        Assert.Same(layer.Mask!.Pixels,result.Mask!.Pixels);Assert.Equal(layer.Transform,result.Mask.Placement);Assert.Null(layer.Mask.Placement);
        var session=new EditorSession(doc);session.Apply(_=>next);session.Undo();Assert.Same(doc,session.Document);session.Redo();Assert.Same(next,session.Document);
        Assert.Throws<ArgumentOutOfRangeException>(()=>MotionBlur.Apply(doc,layer.Id,2001,0));Assert.Throws<ArgumentOutOfRangeException>(()=>MotionBlur.Apply(doc,layer.Id,10,91));
        using var token=new CancellationTokenSource();token.Cancel();Assert.Throws<OperationCanceledException>(()=>MotionBlur.Apply(doc,layer.Id,10,0,cancellation:token.Token));
    }
    [Fact] public void MaximumDistanceUsesBoundedPreviewAndKeepsSourceImmutable()
    {
        var doc=Document.Create(64,64);var layer=doc.Layers[0] with{Pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,1,1,1),64,64)};doc=doc.Replace(layer);
        var shown=MotionBlur.Preview(doc,layer.Id,2000,90).Layers[0];
        Assert.Equal(2048,shown.Pixels.Width);Assert.Equal(2048,shown.Pixels.Height);Assert.NotEmpty(shown.Pixels.Tiles);
        Assert.Equal(255,Alpha(layer,32,32));Assert.Same(layer,doc.Layers[0]);
    }
}
