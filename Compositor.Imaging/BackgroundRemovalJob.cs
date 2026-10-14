using Compositor.Core;
namespace Compositor.Imaging;

/// <summary>One edit owns one immutable source and caches its inferred matte. Call serially.</summary>
public sealed class BackgroundRemovalJob(Document original,Guid layerId,Func<Raster,CancellationToken,LayerMask> predict)
{
    private LayerMask? matte;
    public Document Prepare(MatteSettings settings,bool preview,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();settings.Validate();
        var source=original.Layers.First(l=>l.Id==layerId).Pixels;
        if(matte is null){var inferred=predict(source,cancellation);cancellation.ThrowIfCancellationRequested();matte=inferred;}
        return SubjectMask.Apply(original,layerId,MatteRefinement.Apply(matte,source,settings,preview,cancellation),cancellation);
    }
}
