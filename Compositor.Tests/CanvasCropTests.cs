using Compositor.Core;
using Compositor.Imaging;
using Xunit;
using SkiaSharp;
namespace Compositor.Tests;
public sealed class CanvasCropTests
{
    [Fact] public void CropKeepsOffCanvasPixelsMasksEffectsAndMovesEveryAbsoluteTransformOnce()
    {
        var d=Document.Create(100,80);var image=d.Layers[0] with{Pixels=LayerMask.Solid(100,80,40).Pixels,Mask=LayerMask.Solid(1,1,80),Effects=new(ColorOverlay:new(.3,.5,.8,.4)),Transform=new(10,15,100,80,30,true)};
        d=d.Replace(image);var group=Layer.Group("Group",100,80);d=LayerHierarchy.Wrap(d,image.Id,group);
        d=d with{Layers=d.Layers.Add(Layer.ExposureLayer(100,80)),Selection=SelectionGeometry.Box(20,20,40,40)};
        var cropped=CanvasCrop.Apply(d,new(20,10,50,40));
        Assert.Equal(50,cropped.Width);Assert.Equal(40,cropped.Height);Assert.Null(cropped.Selection);
        for(int i=0;i<d.Layers.Length;i++)
        {
            Assert.Same(d.Layers[i].Pixels,cropped.Layers[i].Pixels);Assert.Same(d.Layers[i].Mask,cropped.Layers[i].Mask);
            Assert.Equal(d.Layers[i].Transform.X-20,cropped.Layers[i].Transform.X);
            Assert.Equal(d.Layers[i].Transform.Y-10,cropped.Layers[i].Transform.Y);
            Assert.Equal(d.Layers[i] with{Transform=cropped.Layers[i].Transform},cropped.Layers[i]);
        }
        Assert.Equal(0,EditorSession.UndoBytesRequired(d,cropped));
        var session=new EditorSession(d);session.Apply(doc=>CanvasCrop.Apply(doc,new(20,10,50,40)));
        var applied=session.Document;session.Undo();Assert.Same(d,session.Document);session.Redo();Assert.Same(applied,session.Document);
    }
    [Fact] public void ExtensionTranslatesExistingContentWithoutRasterizing()
    {
        var d=Document.Create(10,8);var next=CanvasCrop.Apply(d,new(-5,-3,20,20));
        Assert.Equal(5,next.Layers[0].Transform.X);Assert.Equal(3,next.Layers[0].Transform.Y);
        Assert.Same(d.Layers[0].Pixels,next.Layers[0].Pixels);
        Assert.Same(d,CanvasCrop.Apply(d,new(0,0,10,8)));
    }
    [Fact] public void InvalidCropDoesNotModifySession()
    {
        var d=Document.Create(10,10);var s=new EditorSession(d);
        Assert.Throws<InvalidDataException>(()=>s.Apply(doc=>CanvasCrop.Apply(doc,new(0,0,30000,30000))));
        Assert.Same(d,s.Document);Assert.Equal(0,s.UndoCount);Assert.False(s.InTransaction);
    }
    [Fact] public void CreateStandardizesNegativeDragsSnapsPixelsAndPreservesRatio()
    {
        Assert.Equal(new CropFrame(2,3,8,7),CropGeometry.Create(new(10,10),new(2.3,3.2)));
        Assert.Equal(new CropFrame(10,10,16,9),CropGeometry.Create(new(10,10),new(26,12),16d/9));
        Assert.Equal(new CropFrame(2,6,16,8),CropGeometry.Create(new(10,10),new(18,14),symmetric:true));
        Assert.Equal(new CropFrame(4,4,1,1),CropGeometry.Create(new(4,4),new(4,4)));
    }
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)][InlineData(4)][InlineData(5)][InlineData(6)][InlineData(7)]
    public void AllHandlesHaveNoGrabJumpAndSymmetricResizeKeepsCenter(int index)
    {
        var frame=new CropFrame(10,20,100,60);var h=CropGeometry.Handles[index];var start=new PointD(10+h.X*100+2,20+h.Y*60-2);
        Assert.Equal(frame,CropGeometry.Resize(frame,index,start,start));
        var symmetric=CropGeometry.Resize(frame,index,start,new(start.X+10,start.Y+10),symmetric:true);
        Assert.Equal(60,symmetric.X+symmetric.Width/2d);Assert.Equal(50,symmetric.Y+symmetric.Height/2d);
    }
    [Fact] public void CrossingAnOppositeEdgeAndRatioResizeMatchOriginalAnchors()
    {
        var frame=new CropFrame(0,0,100,50);
        Assert.Equal(new CropFrame(100,0,20,50),CropGeometry.Resize(frame,7,new(0,25),new(120,25)));
        var scaled=CropGeometry.Resize(frame,4,new(100,50),new(150,75),lockRatio:true);
        Assert.Equal(new CropFrame(0,0,150,75),scaled);
        Assert.Equal(new CropFrame(0,-25,100,100),CropGeometry.WithRatio(frame,1));
    }
    [Fact] public void GeometryRejectsNonfiniteAndOversizedFrames()
    {
        Assert.Throws<ArgumentException>(()=>CropGeometry.Snap(double.NaN,0,10,10));
        Assert.Throws<ArgumentOutOfRangeException>(()=>CropGeometry.Create(new(0,0),new(10,10),0));
        Assert.Throws<InvalidDataException>(()=>CropGeometry.Snap(0,0,30001,1));
    }
    [Fact] public void RenderMatchesOriginalRegionAndSavedCropRetainsHiddenPixels()
    {
        var d=Document.Create(16,12);
        var bytes=new byte[16*12*4];
        for(int y=0;y<12;y++)for(int x=0;x<16;x++)
        {int i=(y*16+x)*4;bytes[i]=(byte)(x*7);bytes[i+1]=(byte)(y*9);bytes[i+2]=40;bytes[i+3]=128;}
        d=d.Replace(d.Layers[0] with{Pixels=Raster.FromRgba(16,12,bytes),Mask=LayerMask.Solid(16,12,180)});
        var crop=CanvasCrop.Apply(d,new(3,2,8,6));
        using var renderer=new CanvasRenderer();
        using var original=new SKBitmap(CanvasRenderer.Info(16,12));
        using(var canvas=new SKCanvas(original)){canvas.Clear();renderer.Draw(canvas,d);}
        using var result=new SKBitmap(CanvasRenderer.Info(8,6));
        using(var canvas=new SKCanvas(result)){canvas.Clear();renderer.Draw(canvas,crop);}
        for(int y=0;y<6;y++)for(int x=0;x<8;x++)Assert.Equal(original.GetPixel(x+3,y+2),result.GetPixel(x,y));
        string root=Path.Combine(Path.GetTempPath(),"compositor-crop-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            ProjectStore.Save(crop,crop.Layers[0].Id,root);
            var loaded=ProjectStore.Load(root).Document;
            Assert.Equal(8,loaded.Width);Assert.Equal(6,loaded.Height);
            Assert.Equal(crop.Layers[0].Transform,loaded.Layers[0].Transform);
            Assert.Equal(bytes,loaded.Layers[0].Pixels.ToRgba());
            Assert.Equal(d.Layers[0].Mask!.Pixels.ToRgba(),loaded.Layers[0].Mask!.Pixels.ToRgba());
            var expanded=CanvasCrop.Apply(loaded,new(-3,-2,16,12));
            Assert.Equal(d.Layers[0].Transform,expanded.Layers[0].Transform);
        }
        finally
        {
            string full=Path.GetFullPath(root),temp=Path.GetFullPath(Path.GetTempPath());
            if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&Path.GetFileName(full).StartsWith("compositor-crop-")&&Directory.Exists(full))Directory.Delete(full,true);
        }
    }
}
