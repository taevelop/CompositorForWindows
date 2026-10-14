using Compositor.Core;

namespace Compositor.Imaging;

/// <summary>Captures raw document pixels, performs warp dabs, then copies the result along the dab path.</summary>
public sealed class WarpLayerStroke
{
    private readonly Document original;
    private readonly Guid layerId;
    private readonly BrushSettings settings;
    private readonly WarpRasterStroke warp;
    private Document? completed;
    public Raster WorkingPixels => warp.Pixels;
    public IReadOnlyList<PointD> Points => warp.Points;
    public WarpLayerStroke(Document document, Guid layerId, BrushSettings settings, SmearMode mode, CancellationToken cancellation = default)
    {
        document.Validate();var layer = document.Layers.First(l => l.Id == layerId);
        if (mode is not (SmearMode.Smudge or SmearMode.Liquify)) throw new ArgumentException("Select Smudge or Liquify.");
        if (layer.IsGroup || layer.IsAdjustment) throw new InvalidOperationException("Smudge and Liquify require image pixels.");
        original = document;this.layerId = layerId;this.settings = settings;
        var sample = CloneStamp.Capture(document, layerId, new(), cancellation);
        warp = new(sample, settings, mode, cancellation);
    }
    public void Append(PointD point, CancellationToken cancellation = default)
    {
        if (completed is not null) throw new InvalidOperationException("Warp stroke is already complete.");
        warp.Append(point, cancellation);
    }
    public Document Complete(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();if (warp.IsFaulted) throw new InvalidOperationException("Discard a failed warp stroke.");if (completed is not null) return completed;
        if (warp.Points.Count == 0) return completed = original;
        var tip = settings with { Diameter = Math.Max(2, settings.Diameter) + 4, Hardness = 1, Opacity = 1, Erase = false };
        var stroke = new CloneStroke(original, layerId, tip, warp.Pixels, new(), replaceSample: true);
        foreach (var point in warp.Points) { cancellation.ThrowIfCancellationRequested();stroke.Append(point); }
        var result = stroke.Complete();cancellation.ThrowIfCancellationRequested();return completed = result;
    }
}
