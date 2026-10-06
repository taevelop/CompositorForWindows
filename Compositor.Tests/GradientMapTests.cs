using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;
namespace Compositor.Tests;
public sealed class GradientMapTests
{
    private static byte[] Render(Document d) { using var r=new CanvasRenderer();using var i=r.Flatten(d);using var b=new SKBitmap(CanvasRenderer.Info(i.Width,i.Height));Assert.True(i.ReadPixels(b.Info,b.GetPixels(),b.RowBytes,0,0));return b.GetPixelSpan().ToArray(); }
    [Fact] public void NativeKernelMapsLuminanceAndKeepsAlpha()
    {
        byte[] pixels=[255,0,0,255, 0,255,0,255, 0,0,255,255, 64,64,64,128, 0,0,0,0];
        NativePixels.GradientMap(pixels,5,new GradientMapAdjustment().Table());
        Assert.Equal(new byte[]{54,54,54,255,182,182,182,255,18,18,18,255,64,64,64,128,0,0,0,0},pixels);
    }
    [Fact] public void ColorsReverseAndInterpolationUseOriginalRounding()
    {
        var s=new GradientMapAdjustment {Shadows=new(.125,.25,.375),Highlights=new(.875,.75,.625)};
        byte[] pixels=[0,0,0,255,255,255,255,255];
        NativePixels.GradientMap(pixels,2,s.Table());
        Assert.Equal(new byte[]{32,64,96,255,223,191,159,255},pixels);
        byte[] reversed=[0,0,0,255,255,255,255,255];
        NativePixels.GradientMap(reversed,2,(s with {Reversed=true}).Table());
        Assert.Equal(pixels.AsSpan(0,4).ToArray(),reversed.AsSpan(4,4).ToArray());
        Assert.Equal(pixels.AsSpan(4,4).ToArray(),reversed.AsSpan(0,4).ToArray());
    }
    [Fact] public void MaskOpacityAndRoundtripPreserveSourceAndOutput()
    {
        var d=Document.Create(2,1);
        d=d.Replace(d.Layers[0] with {Pixels=Raster.FromRgba(2,1,[255,0,0,255,64,0,0,128])});
        var source=d.Layers[0].Pixels;
        var a=Layer.GradientMapLayer(2,1) with {GradientMap=new(){Shadows=new(0,.2,.4),Highlights=new(1,.7,.3),Reversed=true},Opacity=.5};
        Assert.Equal(Render(d),Render(d with {Layers=d.Layers.Add(a with {Mask=LayerMask.Solid(2,1,0)})}));
        var mapped=d with {Layers=d.Layers.Add(a)};
        var output=Render(mapped);Assert.NotEqual(Render(d),output);Assert.Equal(128,output[7]);Assert.Same(source,mapped.Layers[0].Pixels);
        var maskLayer=a with {Mask=LayerMask.Solid(2,1,0)};
        var stroke=new MaskStroke(maskLayer,new(2,1,1,255,255,255),2,1);stroke.Append(new(1,.5));Assert.NotEqual(maskLayer.Mask.Pixels.ToRgba(),stroke.Mask.Pixels.ToRgba());
        var root=Path.Combine(Path.GetTempPath(),"CompositorGM-"+Guid.NewGuid().ToString("N")+".comp");
        try {ProjectStore.Save(mapped,null,root);var loaded=ProjectStore.Load(root).Document;Assert.Equal(a.GradientMap,loaded.Layers[1].GradientMap);Assert.Equal(output,Render(loaded));}
        finally {var full=Path.GetFullPath(root);if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("CompositorGM-"))throw new IOException("Unsafe cleanup.");if(Directory.Exists(full))Directory.Delete(full,true);}
    }
    [Theory][InlineData(-.01)][InlineData(1.01)][InlineData(double.NaN)]
    public void InvalidColorIsRejected(double value)=>Assert.Throws<InvalidDataException>(()=>new GradientMapAdjustment{Shadows=new(value,0,0)}.Validate());
}
