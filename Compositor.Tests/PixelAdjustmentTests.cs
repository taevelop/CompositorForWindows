using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;
namespace Compositor.Tests;
public sealed class PixelAdjustmentTests
{
    private static Layer Adjustment(int kind,int w,int h)=>kind switch
    {
        0=>Layer.HueSaturationLayer(w,h) with{HueSaturation=new(60,10,0)},
        1=>Layer.ExposureLayer(w,h) with{Exposure=new(1,0,1)},
        2=>Layer.LevelsLayer(w,h) with{Levels=new(){RGB=new(Gamma:1.5)}},
        3=>Layer.CurvesLayer(w,h) with{Curves=new(){RGB=new(new(0,0),new(128,180),new(255,255))}},
        4=>Layer.InvertLayer(w,h),
        5=>Layer.BlackWhiteLayer(w,h),
        6=>Layer.ColorBalanceLayer(w,h) with{ColorBalance=new(MidCyanRed:30)},
        7=>Layer.GrainLayer(w,h) with{Grain=new(50,2,70,123)},
        _=>Layer.GradientMapLayer(w,h) with{GradientMap=new(){Shadows=new(.1,.2,.4),Highlights=new(.9,.7,.3)}}
    };
    private static byte[] Render(Document d){using var r=new CanvasRenderer();using var image=r.Flatten(d);using var bitmap=new SKBitmap(CanvasRenderer.Info(d.Width,d.Height));Assert.True(image.ReadPixels(bitmap.Info,bitmap.GetPixels(),bitmap.RowBytes,0,0));return bitmap.GetPixelSpan().ToArray();}
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)][InlineData(7)][InlineData(8)]
    public void PixelResultMatchesAdjustmentOnSourceAndPreservesAllAlpha(int kind)
    {
        const int w=260,h=2;var bytes=new byte[w*h*4];
        for(int i=0;i<w*h;i++){byte a=(byte)(i%256);bytes[i*4]=(byte)(a*.8);bytes[i*4+1]=(byte)(a*.4);bytes[i*4+2]=(byte)(a*.2);bytes[i*4+3]=a;}
        var source=Raster.FromRgba(w,h,bytes);var adj=Adjustment(kind,w,h);
        var result=PixelAdjustments.Apply(source,adj);var actual=result.ToRgba();
        var d=Document.Create(w,h);d=d.Replace(d.Layers[0] with{Pixels=source});d=d with{Layers=d.Layers.Add(adj)};
        Assert.Equal(Render(d),actual);Assert.Equal(bytes,source.ToRgba());
        for(int i=3;i<bytes.Length;i+=4)Assert.Equal(bytes[i],actual[i]);
    }
    [Fact] public void UnchangedTilesAreSharedAndNeutralFilterReturnsSource()
    {
        var bytes=new byte[512*4];
        for(int x=0;x<512;x++){bytes[x*4]=(byte)(x<256?100:200);bytes[x*4+1]=100;bytes[x*4+2]=100;bytes[x*4+3]=255;}
        var source=Raster.FromRgba(512,1,bytes);
        Assert.Same(source,PixelAdjustments.Apply(source,Layer.ExposureLayer(512,1)));
        var result=PixelAdjustments.Apply(source,Adjustment(0,512,1));
        Assert.Same(source.Tiles[new(0,0)],result.Tiles[new(0,0)]);
        Assert.NotSame(source.Tiles[new(1,0)],result.Tiles[new(1,0)]);
    }
    [Fact] public void CancellationAndInvalidSettingsDoNotModifySource()
    {
        var source=Raster.FromRgba(1,1,[100,40,20,128]);var before=source.ToRgba();
        using var c=new CancellationTokenSource();c.Cancel();
        Assert.Throws<OperationCanceledException>(()=>PixelAdjustments.Apply(source,Adjustment(1,1,1),c.Token));
        Assert.Throws<ArgumentException>(()=>PixelAdjustments.Apply(source,Layer.Blank("bad",1,1)));
        Assert.Throws<InvalidDataException>(()=>PixelAdjustments.Apply(source,Layer.ExposureLayer(1,1) with{Exposure=new(double.NaN)}));
        Assert.Equal(before,source.ToRgba());
    }
    [Fact] public void BakedPixelsSaveWithoutAdjustmentAndKeepMaskEffects()
    {
        var d=Document.Create(2,1);var source=d.Layers[0] with{Pixels=Raster.FromRgba(2,1,[160,60,20,180,20,80,40,128]),Mask=LayerMask.Solid(2,1,200),Effects=new(ColorOverlay:new(.2,.4,.6,.3)),Transform=new(0,0,2,1,10)};
        var baked=source with{Pixels=PixelAdjustments.Apply(source.Pixels,Layer.InvertLayer(2,1))};d=d.Replace(baked);
        string root=Path.Combine(Path.GetTempPath(),"CompositorPixel-"+Guid.NewGuid().ToString("N")+".comp");
        try{
            ProjectStore.Save(d,null,root);var loaded=ProjectStore.Load(root).Document;
            Assert.False(loaded.Layers[0].IsAdjustment);Assert.Equal(baked.Pixels.ToRgba(),loaded.Layers[0].Pixels.ToRgba());
            Assert.Equal(baked.Effects,loaded.Layers[0].Effects);Assert.Equal(baked.Mask!.Pixels.ToRgba(),loaded.Layers[0].Mask!.Pixels.ToRgba());
            Assert.Equal(Render(d),Render(loaded));
        }
        finally{string full=Path.GetFullPath(root);if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("CompositorPixel-"))throw new IOException("Unsafe cleanup.");if(Directory.Exists(full))Directory.Delete(full,true);}
    }
}
