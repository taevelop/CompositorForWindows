using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class PreparedDocumentRenderTests
{
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
    [Fact] public async Task CancelledOrDisposedPreparationCannotBeInstalled()
    {
        var doc=Create();using var cancellation=new CancellationTokenSource();cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>PreparedDocumentRender.CreateAsync(doc,cancellation.Token));
        var prepared=await PreparedDocumentRender.CreateAsync(doc);prepared.Dispose();prepared.Dispose();
        using var viewport=new ViewportRenderer();Assert.Throws<ObjectDisposedException>(()=>viewport.InstallPrepared(prepared,doc));
    }
}
