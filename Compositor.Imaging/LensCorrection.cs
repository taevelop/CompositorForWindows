using Compositor.Core;

namespace Compositor.Imaging;

public static class LensCorrection
{
    public static Document Preview(Document document, Guid layerId, double distortion, CancellationToken cancellation = default)
    {
        document.Validate();
        if (!double.IsFinite(distortion) || distortion is < -100 or > 100)
            throw new ArgumentOutOfRangeException(nameof(distortion));
        cancellation.ThrowIfCancellationRequested();
        var layer = document.Layers.First(l => l.Id == layerId);
        if (layer.IsGroup || layer.IsAdjustment) throw new InvalidOperationException("Select image pixels for lens correction.");
        int width = layer.Pixels.Width, height = layer.Pixels.Height;
        if (Math.Max(width, height) <= 2048) return Apply(document, layerId, distortion, cancellation);
        if (distortion == 0 || layer.Pixels.Tiles.Count == 0) return document;
        var selection = document.Selection is null ? null : SelectionCoverage.Create(document.Selection, document.Width, document.Height);
        if (selection?.IsEmpty == true) return document;
        double factor = 2048d / Math.Max(width, height);
        int w = Math.Max(1, (int)(width * factor)), h = Math.Max(1, (int)(height * factor));
        var bytes = new byte[w * h * 4];
        for (int y = 0; y < h; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            double sy = (y + .5) * height / h - .5;
            int top = (int)Math.Floor(sy); double fy = sy - top;
            for (int x = 0; x < w; x++)
            {
                double sx = (x + .5) * width / w - .5;
                int left = (int)Math.Floor(sx); double fx = sx - left;
                var a = Sample(layer.Pixels, left, top); var b = Sample(layer.Pixels, left + 1, top);
                var c = Sample(layer.Pixels, left, top + 1); var d = Sample(layer.Pixels, left + 1, top + 1);
                for (int channel = 0; channel < 4; channel++)
                    bytes[(y * w + x) * 4 + channel] = (byte)Math.Round((a[channel] * (1 - fx) + b[channel] * fx) * (1 - fy) + (c[channel] * (1 - fx) + d[channel] * fx) * fy, MidpointRounding.AwayFromZero);
            }
        }
        var small = Raster.FromRgba(w, h, bytes);
        var corrected = SelectionPixels.Blend(small, Apply(small, distortion, cancellation), layer.Transform, selection, cancellation);
        var mask = layer.Mask;
        if (mask is { Placement: null } && (mask.Pixels.Width != 1 || mask.Pixels.Height != 1))
            mask = mask with { Placement = layer.Transform };
        cancellation.ThrowIfCancellationRequested();
        return document.Replace(layer with { Pixels = corrected, Mask = mask });
    }

    private static readonly byte[] transparent = [0, 0, 0, 0];
    private static ReadOnlySpan<byte> Sample(Raster source, int x, int y) =>
        x < 0 || y < 0 || x >= source.Width || y >= source.Height ? transparent :
        source.Tiles.TryGetValue(new(x / 256, y / 256), out var tile) ? tile.Bytes.Slice(((y % 256) * 256 + x % 256) * 4, 4) : transparent;

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
