using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;
public sealed class SoftEffectTests
{
    [Fact] public void CoverageMatchesIndependentDecimalFormulaForAllAlphas()
    {
        for(int a=0;a<256;a++)for(int b=0;b<256;b++)
        {
            Assert.Equal((byte)Math.Round(a*(255-b)/255m),SoftEffectProcessor.Coverage((byte)a,(byte)b,true));
            Assert.Equal((byte)Math.Round(b*(255-a)/255m),SoftEffectProcessor.Coverage((byte)a,(byte)b,false));
        }
    }
    private static Document Sample()
    {
        var d=Document.Create(40,40);var pixels=new byte[10*10*4];
        for(int p=0;p<pixels.Length;p+=4) {pixels[p]=255;pixels[p+3]=255;}
        return d.Replace(d.Layers[0] with {Pixels=Raster.FromRgba(10,10,pixels),Transform=new(15,15,10,10)});
    }
    private static byte[] Bytes(SKImage image) {using var b=new SKBitmap(CanvasRenderer.Info(image.Width,image.Height));Assert.True(image.ReadPixels(b.Info,b.GetPixels(),b.RowBytes,0,0));return b.GetPixelSpan().ToArray();}
    private static byte[] Render(Document d) {using var r=new CanvasRenderer();using var image=r.Flatten(d);return Bytes(image);}
    [Fact] public void InnerShadowFallsInsideEdgeAndGlowStaysOutsideOpaqueShape()
    {
        var d=Sample();var l=d.Layers[0];
        var inner=Render(d.Replace(l with {Effects=new(InnerShadow:new(90,3,0,Opacity:1))}));
        Assert.Equal(0,inner[(15*40+20)*4]);Assert.Equal(255,inner[(15*40+20)*4+3]);
        Assert.Equal(255,inner[(20*40+20)*4]);Assert.Equal(0,inner[(14*40+20)*4+3]);
        var glow=Render(d.Replace(l with {Effects=new(OuterGlow:new(6,0,1,0,1))}));
        Assert.True(glow[(14*40+20)*4+1]>0);Assert.Equal(0,glow[(20*40+20)*4+1]);
        Assert.Equal(Render(d),Render(d.Replace(l with {Effects=new(OuterGlow:new(6,Enabled:false),InnerShadow:new(Enabled:false))})));
    }
    [Fact] public void FiveEffectsMaskRotationAndCacheAgreeWithFreshExport()
    {
        var d=Sample();var effects=new LayerEffects(new(.2,.3,.4,.5),new(120,4,2),new(2,.8,.5,.2,Inside:true),
            new(90,2,2),new(5,.2,.8,.4,.7));
        var layer=d.Layers[0] with {Effects=effects,Transform=d.Layers[0].Transform with {Rotation=25}};
        d=d.Replace(layer);using var viewport=new ViewportRenderer();
        foreach(var current in new[]{d,d.Replace(layer with {Mask=LayerMask.Solid(10,10,128)}),
            d.Replace(layer with {Effects=effects with {InnerShadow=new(180,4,3),OuterGlow=new(2)}}),d})
        {using var image=viewport.Render(current,40,40,1,0,0);Assert.Equal(Render(current),Bytes(image));}
        Assert.All(Render(d.Replace(layer with {Mask=LayerMask.Solid(1,1,0)})),b=>Assert.Equal((byte)0,b));
    }
    [Fact] public void SavePreservesAllFiveEffectsAndOptionalFlags()
    {
        var d=Sample();d=d.Replace(d.Layers[0] with {Effects=new(new(),new(),new(),new(45,10,12,.12,.3,.5,.4,false),new(20,.2,.3,.4,.75))});
        string root=Path.Combine(Path.GetTempPath(),"CompositorSoft-"+Guid.NewGuid().ToString("N")+".comp");
        try {ProjectStore.Save(d,null,root);var loaded=ProjectStore.Load(root).Document;Assert.Equal(d.Layers[0].Effects,loaded.Layers[0].Effects);Assert.Equal(Render(d),Render(loaded));}
        finally
        {
            string full=Path.GetFullPath(root),parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("CompositorSoft-"))throw new IOException("Unsafe cleanup.");
            if(Directory.Exists(full))Directory.Delete(full,true);
        }
    }
    [Theory][InlineData(-1)][InlineData(501)][InlineData(double.NaN)]
    public void InvalidGlowSizeIsRejected(double size)=>Assert.Throws<InvalidDataException>(()=>new OuterGlowEffect(size).Validate());
}
