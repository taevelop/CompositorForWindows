using Compositor.Core;

namespace Compositor.Imaging;

public static class LensCorrection
{
    public static Raster Apply(Raster source, double distortion, CancellationToken cancellation = default)
    {
        if (!double.IsFinite(distortion) || distortion is < -100 or > 100)
            throw new ArgumentOutOfRangeException(nameof(distortion));
        cancellation.ThrowIfCancellationRequested();
        if (distortion == 0 || source.Tiles.Count == 0) return source;
        var input = source.ToRgba();
        var output = new byte[input.Length];
        cancellation.ThrowIfCancellationRequested();
        NativePixels.Lens(input, output, source.Width, source.Height, distortion / 100 * .35);
        cancellation.ThrowIfCancellationRequested();
        if (input.AsSpan().SequenceEqual(output)) return source;
        // A warp can populate formerly empty tiles; rebuild over the entire output grid.
        var result = Raster.FromRgba(source.Width, source.Height, output);
        cancellation.ThrowIfCancellationRequested();
        return result;
    }

    public static Document Apply(Document document, Guid layerId, double distortion, CancellationToken cancellation = default)
    {
        document.Validate();
        var layer = document.Layers.First(l => l.Id == layerId);
        if (layer.IsGroup || layer.IsAdjustment) throw new InvalidOperationException("Select image pixels for lens correction.");
        var corrected = Apply(layer.Pixels, distortion, cancellation);
        var selection = document.Selection is null ? null : SelectionCoverage.Create(document.Selection, document.Width, document.Height);
        var pixels = SelectionPixels.Blend(layer.Pixels, corrected, layer.Transform, selection, cancellation);
        if (ReferenceEquals(pixels, layer.Pixels)) return document;
        var next = document.Replace(layer with { Pixels = pixels });
        next.Validate();
        if (EditorSession.UndoBytesRequired(document, next) > EditorSession.MaxHistoryBytes)
            throw new InvalidOperationException("Lens correction exceeds the 256 MiB Undo limit.");
        cancellation.ThrowIfCancellationRequested();
        return next;
    }
}
