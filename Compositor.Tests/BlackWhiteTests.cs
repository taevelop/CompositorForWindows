using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;
public sealed class BlackWhiteTests
{
    private static byte[] Render(Document d){using var r=new CanvasRenderer();using var i=r.Flatten(d);using var b=new SKBitmap(CanvasRenderer.Info(i.Width,i.Height));Assert.True(i.ReadPixels(b.Info,b.GetPixels(),b.RowBytes,0,0));return b.GetPixelSpan().ToArray();}
    private static Document Sample()
    {
        var d=Document.Create(6,1);
        byte[] pixels=[255,0,0,255,255,255,0,255,0,255,0,255,0,255,255,255,0,0,255,255,255,0,255,255];
        return d.Replace(d.Layers[0] with {Pixels=Raster.FromRgba(6,1,pixels)});
    }
    [Fact] public void SixFamiliesMatchOriginalDefaults()
    {
        var d=Sample();var layer=Layer.BlackWhiteLayer(6,1);var result=Render(d with {Layers=d.Layers.Add(layer)});
        int[] values=[102,153,102,153,51,204];
        for(int p=0;p<6;p++){Assert.Equal(values[p],result[p*4]);Assert.Equal(result[p*4],result[p*4+1]);Assert.Equal(result[p*4],result[p*4+2]);Assert.Equal(255,result[p*4+3]);}
    }
    [Fact] public void InvertPreservesEveryAlphaAndIsItsOwnInverse()
    {
        var d=Document.Create(256,1);var pixels=new byte[1024];
        for(int a=0;a<256;a++){pixels[a*4]=(byte)(a/3);pixels[a*4+1]=(byte)(a/2);pixels[a*4+2]=(byte)a;pixels[a*4+3]=(byte)a;}
        d=d.Replace(d.Layers[0] with {Pixels=Raster.FromRgba(256,1,pixels)});
        var invert=Layer.InvertLayer(256,1);var next=d with {Layers=d.Layers.Add(invert)};var result=Render(next);
        for(int a=0;a<256;a++){for(int c=0;c<3;c++)Assert.Equal(a-pixels[a*4+c],result[a*4+c]);Assert.Equal(a,result[a*4+3]);}
        Assert.Equal(pixels,Render(next with {Layers=next.Layers.Add(Layer.InvertLayer(256,1))}));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void MaskOpacityAndTransformRestrictAdjustment(bool invert)
    {
        var d=Sample();var adjustment=invert?Layer.InvertLayer(6,1):Layer.BlackWhiteLayer(6,1);
        Assert.Equal(Render(d),Render(d with {Layers=d.Layers.Add(adjustment with {Mask=LayerMask.Solid(1,1,0)})}));
        var full=Render(d with {Layers=d.Layers.Add(adjustment)});var original=Render(d);
        var half=Render(d with {Layers=d.Layers.Add(adjustment with {Opacity=.5})});
        for(int p=0;p<full.Length;p++)Assert.InRange((int)half[p],(int)Math.Floor((full[p]+original[p])/2d),(int)Math.Ceiling((full[p]+original[p])/2d));
        var masked=adjustment with {Mask=LayerMask.Solid(6,1,255),Transform=new(3,0,6,1)};
        var shifted=Render(d with {Layers=d.Layers.Add(masked)});
        Assert.Equal(original[..12],shifted[..12]);Assert.Equal(full[12..],shifted[12..]);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void NewAdjustmentsAllowMaskPaintingOnly(bool invert)
    {
        var layer=(invert?Layer.InvertLayer(40,40):Layer.BlackWhiteLayer(40,40)) with {Mask=LayerMask.Solid(40,40,255)};
        var brush=new BrushSettings(20,1,1,0,0,0);
        Assert.Throws<InvalidOperationException>(()=>new BrushStroke(layer,brush,40,40));
        var mask=new MaskStroke(layer,brush,40,40);mask.Append(new(20,20));
        Assert.NotEqual(layer.Mask.Pixels.ToRgba(),mask.Mask.Pixels.ToRgba());Assert.Empty(layer.Pixels.Tiles);
    }
    [Fact] public void TintKeepsAlphaAndPremultipliedRange()
    {
        var pixels=new byte[256*4];for(int a=0;a<256;a++){pixels[a*4]=(byte)(a/2);pixels[a*4+1]=(byte)(a/3);pixels[a*4+2]=(byte)(a/5);pixels[a*4+3]=(byte)a;}
        NativePixels.BlackWhite(pixels,256,new BlackWhiteAdjustment().Weights,1,40,.6);
        for(int a=0;a<256;a++){Assert.Equal(a,pixels[a*4+3]);for(int c=0;c<3;c++)Assert.InRange(pixels[a*4+c],(byte)0,(byte)a);}
        Assert.True(pixels[255*4]>pixels[255*4+2]);
    }
    [Fact] public void BothKindsSaveAndUndoWithoutDuplicatingSource()
    {
        var d=Sample();d=d with {Layers=d.Layers.Add(Layer.BlackWhiteLayer(6,1) with {BlackWhite=new(Tint:true)}).Add(Layer.InvertLayer(6,1))};
        string root=Path.Combine(Path.GetTempPath(),"CompositorBW-"+Guid.NewGuid().ToString("N")+".comp");
        try{
            ProjectStore.Save(d,null,root);var loaded=ProjectStore.Load(root).Document;
            Assert.True(loaded.Layers[2].Invert);Assert.Equal(d.Layers[1].BlackWhite,loaded.Layers[1].BlackWhite);Assert.Equal(Render(d),Render(loaded));
            var s=new EditorSession(loaded);s.Apply(x=>x.Replace(x.Layers[1] with {BlackWhite=new(Reds:80)}));Assert.Equal(0,s.HistoryRetainedBytes);
            s.Undo();Assert.Equal(d.Layers[1].BlackWhite,s.Document.Layers[1].BlackWhite);s.Redo();Assert.Equal(80,s.Document.Layers[1].BlackWhite!.Reds);
        }finally{
            string full=Path.GetFullPath(root),parent=Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            if(!full.StartsWith(parent,StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("CompositorBW-"))throw new IOException("Unsafe cleanup.");
            if(Directory.Exists(full))Directory.Delete(full,true);
        }
    }
    [Theory][InlineData(-201)][InlineData(301)][InlineData(double.NaN)]
    public void InvalidWeightsAreRejected(double weight)=>Assert.Throws<InvalidDataException>(()=>new BlackWhiteAdjustment(Reds:weight).Validate());
}
