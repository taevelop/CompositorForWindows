using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class LayerFillTests
{
    [Fact] public void ImageFillExpandsToCanvasAndKeepsOriginalImmutable()
    {
        var doc=Document.Create(4,2);var source=doc.Layers[0] with{Pixels=Raster.FromRgba(1,1,[20,30,40,128]),Transform=new(2,1,1,1)};doc=doc.Replace(source);
        var result=LayerFill.Apply(doc,source.Id,10,20,30);var layer=result.Layers[0];
        Assert.Equal(4,layer.Pixels.Width);Assert.Equal(2,layer.Pixels.Height);Assert.Equal(0,layer.Transform.X);
        Assert.Equal(new byte[]{10,20,30,255},layer.Pixels.ToRgba()[..4]);Assert.Equal(new byte[]{20,30,40,128},source.Pixels.ToRgba());
        Assert.Same(result,LayerFill.Apply(result,source.Id,10,20,30));
        var session=new EditorSession(doc);session.Apply(_=>result);session.Undo();Assert.Same(doc,session.Document);session.Redo();Assert.Same(result,session.Document);
    }
    [Fact] public void MaskFillUsesGrayWithoutChangingImageOrTransform()
    {
        var doc=Document.Create(2,1);var source=doc.Layers[0] with{Mask=LayerMask.Solid(1,1,255)};doc=doc.Replace(source);
        var result=LayerFill.Apply(doc,source.Id,64,128,255,true);var layer=result.Layers[0];
        Assert.Same(source.Pixels,layer.Pixels);Assert.Equal(source.Transform,layer.Transform);
        Assert.Equal(new byte[]{64,64,64,255},layer.Mask!.Pixels.ToRgba()[..4]);
    }
    [Fact] public void SelectionAndEmptySelectionRestrictFill()
    {
        var doc=Document.Create(3,1);doc=doc with{Selection=SelectionGeometry.Box(1,0,1,1)};
        var result=LayerFill.Apply(doc,doc.Layers[0].Id,90,80,70);var pixels=result.Layers[0].Pixels.ToRgba();
        Assert.Equal(0,pixels[3]);Assert.Equal(255,pixels[7]);Assert.Equal(0,pixels[11]);
        var empty=doc with{Selection=DocumentSelection.Empty};Assert.Same(empty,LayerFill.Apply(empty,empty.Layers[0].Id,90,80,70));
    }
    [Fact] public void FeatherCrossesTileBoundaryAndKeepsPremultipliedChannels()
    {
        var doc=Document.Create(520,8);doc=doc with{Selection=SelectionGeometry.Box(250,1,12,6) with{Feather=4}};
        var result=LayerFill.Apply(doc,doc.Layers[0].Id,255,128,64);var bytes=result.Layers[0].Pixels.ToRgba();
        Assert.True(result.Layers[0].Pixels.Tiles.ContainsKey(new(0,0)));Assert.True(result.Layers[0].Pixels.Tiles.ContainsKey(new(1,0)));
        int left=(4*520+255)*4,right=(4*520+256)*4;
        Assert.Equal(bytes[left+3],bytes[right+3]);Assert.True(bytes[left+3]>0);
        Assert.Contains(Enumerable.Range(0,520*8),i=>bytes[i*4+3]>0&&bytes[i*4+3]<255);
        for(int i=0;i<bytes.Length;i+=4){Assert.True(bytes[i]<=bytes[i+3]);Assert.True(bytes[i+1]<=bytes[i+3]);Assert.True(bytes[i+2]<=bytes[i+3]);}
        Assert.Equal(0,bytes[3]);
    }
    [Fact] public void RotatedFlippedFillKeepsMaskAtOriginalDocumentPosition()
    {
        var doc=Document.Create(8,8);var layer=doc.Layers[0] with{Pixels=Raster.FromRgba(2,2,new byte[16]),Transform=new(3,3,2,2,Rotation:90,FlipX:true),Mask=LayerMask.Solid(2,2,73)};
        doc=doc.Replace(layer);var result=LayerFill.Apply(doc,layer.Id,200,100,50);var updated=result.Layers[0];
        var originalPoint=layer.Transform.ToDocument(new(.5,.5),2,2);
        var mapped=updated.Transform.ToPixels(originalPoint,updated.Pixels.Width,updated.Pixels.Height);
        int x=(int)Math.Floor(mapped.X),y=(int)Math.Floor(mapped.Y);var mask=updated.Mask!.Pixels.ToRgba();
        Assert.Equal(73,mask[(y*updated.Pixels.Width+x)*4]);
        Assert.Equal(layer.Transform.Rotation,updated.Transform.Rotation);Assert.True(updated.Transform.FlipX);
        Assert.Equal(new byte[]{200,100,50,255},updated.Pixels.ToRgba()[((y*updated.Pixels.Width+x)*4)..][..4]);
    }
    [Fact] public void FilledExpandedLayerRoundTripsPixelsMaskTransformAndOutput()
    {
        var doc=Document.Create(9,7);var layer=doc.Layers[0] with{Pixels=Raster.FromRgba(2,2,new byte[16]),Transform=new(4,2,2,2,Rotation:90,FlipY:true),Mask=LayerMask.Solid(2,2,128)};
        doc=LayerFill.Apply(doc.Replace(layer),layer.Id,23,84,192);
        var root=Path.Combine(Path.GetTempPath(),"CompositorFill-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            ProjectStore.Save(doc,layer.Id,root);var loaded=ProjectStore.Load(root).Document;
            Assert.Equal(doc.Layers[0].Pixels.ToRgba(),loaded.Layers[0].Pixels.ToRgba());
            Assert.Equal(doc.Layers[0].Mask!.Pixels.ToRgba(),loaded.Layers[0].Mask!.Pixels.ToRgba());Assert.Equal(doc.Layers[0].Transform,loaded.Layers[0].Transform);
            using var first=new CanvasRenderer();using var second=new CanvasRenderer();using var a=first.Flatten(doc);using var b=second.Flatten(loaded);
            using var aa=SkiaSharp.SKBitmap.FromImage(a);using var bb=SkiaSharp.SKBitmap.FromImage(b);Assert.Equal(aa.Bytes,bb.Bytes);
        }
        finally
        {
            var full=Path.GetFullPath(root);
            if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("CompositorFill-"))throw new IOException("Unsafe cleanup.");
            if(Directory.Exists(full))Directory.Delete(full,true);
        }
    }
    [Fact] public void UndoBudgetRejectsBeforePublishingAndRespectsSharedTiles()
    {
        var doc=Document.Create(1,1);var layer=doc.Layers[0] with{Pixels=Raster.FromRgba(1,1,[20,30,40,255])};doc=doc.Replace(layer);
        Assert.Throws<InvalidOperationException>(()=>LayerFill.ApplyWithUndoBudget(doc,layer.Id,1,2,3,false,default,0));
        Assert.Equal(new byte[]{20,30,40,255},doc.Layers[0].Pixels.ToRgba());
        Assert.Same(doc,LayerFill.ApplyWithUndoBudget(doc,layer.Id,20,30,40,false,default,0));
        var shared=doc with{Layers=doc.Layers.Add(layer with{Id=Guid.NewGuid()})};
        var result=LayerFill.ApplyWithUndoBudget(shared,layer.Id,1,2,3,false,default,0);
        Assert.Equal(0,EditorSession.UndoBytesRequired(shared,result));Assert.Same(layer.Pixels,result.Layers[1].Pixels);
    }
    [Fact] public void CancellationPreservesSource()
    {
        var doc=Document.Create(2,1);using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>LayerFill.Apply(doc,doc.Layers[0].Id,1,2,3,cancellation:cancellation.Token));
        Assert.Empty(doc.Layers[0].Pixels.Tiles);
    }
}
