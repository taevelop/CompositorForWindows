using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;
namespace Compositor.Tests;
public sealed class ColorBalanceGrainTests
{
    private static byte[] Pixels(int w,int h){var b=new byte[w*h*4];for(int p=0;p<b.Length;p+=4){b[p]=80;b[p+1]=90;b[p+2]=100;b[p+3]=180;}return b;}
    private static byte[] Render(Document d){using var r=new CanvasRenderer();using var i=r.Flatten(d);using var b=new SKBitmap(CanvasRenderer.Info(i.Width,i.Height));Assert.True(i.ReadPixels(b.Info,b.GetPixels(),b.RowBytes,0,0));return b.GetPixelSpan().ToArray();}
    [Fact] public void GrainMatchesWholeImageWhenProcessedAsDocumentSpacePieces()
    {
        var all=Pixels(32,24);var split=all.ToArray();NativePixels.Grain(all,32,24,60,2.5,70,123,0,0,1);
        for(int y=0;y<24;y+=8){var part=split.AsSpan(y*32*4,8*32*4).ToArray();NativePixels.Grain(part,32,8,60,2.5,70,123,0,y,1);part.CopyTo(split,y*32*4);}
        Assert.Equal(all,split);
        var different=Pixels(32,24);NativePixels.Grain(different,32,24,60,2.5,70,124,0,0,1);Assert.NotEqual(all,different);
        for(int p=0;p<all.Length;p+=4){Assert.Equal(180,all[p+3]);for(int c=0;c<3;c++)Assert.InRange(all[p+c],(byte)0,all[p+3]);}
    }
    [Fact] public void ColorBalanceShiftsChosenAxisAndPreservesLuminosity()
    {
        byte[] b=[100,100,100,200];NativePixels.ColorBalance(b,1,[0,0,0],[.2f,0,0],[0,0,0],0);
        Assert.True(b[0]>100);Assert.Equal(100,b[1]);Assert.Equal(100,b[2]);Assert.Equal(200,b[3]);
        byte[] preserved=[100,100,100,200];NativePixels.ColorBalance(preserved,1,[0,0,0],[.2f,0,0],[0,0,0],1);
        Assert.InRange(.299*preserved[0]+.587*preserved[1]+.114*preserved[2],99.5,100.5);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void IdentityMasksAndPaintingKeepSourceImmutable(bool grain)
    {
        var d=Document.Create(32,24);d=d.Replace(d.Layers[0] with {Pixels=Raster.FromRgba(32,24,Pixels(32,24))});
        var a=grain?Layer.GrainLayer(32,24) with {Grain=new(Amount:0)}:Layer.ColorBalanceLayer(32,24);
        Assert.Equal(Render(d),Render(d with {Layers=d.Layers.Add(a)}));
        a=grain?a with {Grain=new(Seed:12)}:a with {ColorBalance=new(MidCyanRed:50)};
        var masked=a with {Mask=LayerMask.Solid(32,24,0)};
        Assert.Equal(Render(d),Render(d with {Layers=d.Layers.Add(masked)}));
        var brush=new MaskStroke(masked,new(10,1,1,255,255,255),32,24);brush.Append(new(16,12));
        Assert.Empty(a.Pixels.Tiles);Assert.NotEqual(masked.Mask!.Pixels.ToRgba(),brush.Mask.Pixels.ToRgba());
    }
    [Fact] public void SavePreservesSeedToneSettingsAndRenderedResult()
    {
        var d=Document.Create(32,24);d=d.Replace(d.Layers[0] with {Pixels=Raster.FromRgba(32,24,Pixels(32,24))});
        d=d with {Layers=d.Layers.Add(Layer.GrainLayer(32,24) with {Grain=new(75,3,70,uint.MaxValue)}).Add(Layer.ColorBalanceLayer(32,24) with {ColorBalance=new(20,-30,40,50,0,10,5,0,-4,false)})};
        var root=Path.Combine(Path.GetTempPath(),"CompositorCG-"+Guid.NewGuid().ToString("N")+".comp");
        try{ProjectStore.Save(d,null,root);var loaded=ProjectStore.Load(root).Document;Assert.Equal(d.Layers[1].Grain,loaded.Layers[1].Grain);Assert.Equal(d.Layers[2].ColorBalance,loaded.Layers[2].ColorBalance);Assert.Equal(Render(d),Render(loaded));}
        finally{var full=Path.GetFullPath(root);if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("CompositorCG-"))throw new IOException("Unsafe cleanup.");if(Directory.Exists(full))Directory.Delete(full,true);}
    }
    [Theory][InlineData(-101)][InlineData(101)][InlineData(double.NaN)]
    public void InvalidBalanceIsRejected(double value)=>Assert.Throws<InvalidDataException>(()=>new ColorBalanceAdjustment(MidCyanRed:value).Validate());
    [Theory][InlineData(-1,1,50)][InlineData(25,.1,50)][InlineData(25,21,50)][InlineData(25,1,101)]
    public void InvalidGrainIsRejected(double a,double s,double r)=>Assert.Throws<InvalidDataException>(()=>new GrainAdjustment(a,s,r).Validate());
}
