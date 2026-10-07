using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class PreparedDocumentRenderTests
{
    [Fact] public async Task ViewportPreparationMatchesFreshRenderAcrossZoomPanAndMasks()
    {
        var doc=Document.Create(600,300);var layer=doc.Layers[0];
        doc=GradientFill.Apply(doc,layer.Id,new(new(.5,.5),new(599.5,299.5),255,0,0,0,0,255,Style:GradientStyle.ForegroundToBackground));
        layer=doc.Layers[0];doc=doc.Replace(layer with{Mask=LayerMask.Solid(600,300,173),Transform=layer.Transform with{Rotation=17}});
        foreach(var view in new[]{new RenderPreparationViewport(320,200,.5f,10,20),new RenderPreparationViewport(320,200,2,-300,-150)})
        {
            using var prepared=await PreparedDocumentRender.CreateAsync(doc,view);
            using var actual=new ViewportRenderer();actual.InstallPrepared(prepared,doc);
            int preparedTiles=actual.TileImageBuildCount;Assert.True(preparedTiles>1);
            using var fresh=new ViewportRenderer();
            using var a=actual.Render(doc,view.Width,view.Height,view.Zoom,view.OffsetX,view.OffsetY);
            Assert.Equal(preparedTiles,actual.TileImageBuildCount);
            using var b=fresh.Render(doc,view.Width,view.Height,view.Zoom,view.OffsetX,view.OffsetY);
            using var ap=a.PeekPixels();using var bp=b.PeekPixels();
            Assert.True(ap.GetPixelSpan().SequenceEqual(bp.GetPixelSpan()));
        }
        Assert.Throws<ArgumentException>(()=>{_=PreparedDocumentRender.CreateAsync(doc,new RenderPreparationViewport(0,10,1,0,0));});
    }
    private static Document Create()
    {
        var doc=Document.Create(2,1);
        doc=doc.Replace(doc.Layers[0] with{Pixels=Raster.FromRgba(2,1,[255,0,0,255,0,0,255,255])});
        return doc with{Layers=doc.Layers.Add(Layer.InvertLayer(2,1))};
    }
    [Fact] public async Task TransfersOnceAndRejectsStaleSnapshotWithoutConsumingCache()
    {
        var doc=Create();using var prepared=await PreparedDocumentRender.CreateAsync(doc);
        using var viewport=new ViewportRenderer();
        Assert.Throws<InvalidOperationException>(()=>viewport.InstallPrepared(prepared,doc with{}));
        var closed=new ViewportRenderer();closed.Dispose();
        Assert.Throws<ObjectDisposedException>(()=>closed.InstallPrepared(prepared,doc));
        viewport.InstallPrepared(prepared,doc);prepared.Dispose();
        Assert.Equal(new SampledColor(0,255,255),viewport.Sample(doc,new(0,0)));
        Assert.Throws<ObjectDisposedException>(()=>viewport.InstallPrepared(prepared,doc));
        using var frame=viewport.Render(doc,2,1,1,0,0);
        using var standalone=new CompositeColorSampler();Assert.Equal(standalone.Sample(doc,new(1,0)),viewport.Sample(doc,new(1,0)));
    }
    [Fact] public void CancelledDrawRestoresCallerCanvasStateAndCanRenderAgain()
    {
        using var renderer=new CanvasRenderer();using var bitmap=new SkiaSharp.SKBitmap(CanvasRenderer.Info(2,1));
        using var canvas=new SkiaSharp.SKCanvas(bitmap);canvas.Save();canvas.Translate(1,0);int count=canvas.SaveCount;var matrix=canvas.TotalMatrix;
        using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(()=>renderer.DrawCancellable(canvas,Create(),cancellation.Token));
        Assert.Equal(count,canvas.SaveCount);Assert.Equal(matrix,canvas.TotalMatrix);
        renderer.Draw(canvas,Create());Assert.Equal(count,canvas.SaveCount);
    }
    [Fact] public async Task CancelledOrDisposedPreparationCannotBeInstalled()
    {
        var doc=Create();using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>PreparedDocumentRender.CreateAsync(doc,cancellation.Token));
        var prepared=await PreparedDocumentRender.CreateAsync(doc);prepared.Dispose();prepared.Dispose();
        using var viewport=new ViewportRenderer();Assert.Throws<ObjectDisposedException>(()=>viewport.InstallPrepared(prepared,doc));
    }
}
