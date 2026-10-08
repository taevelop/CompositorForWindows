using Compositor.Core;

namespace Compositor.Imaging;

public static class ContentAwareFill
{
    public static Document Apply(Document document, Guid layerId, CancellationToken cancellation = default)
    {
        document.Validate(); cancellation.ThrowIfCancellationRequested();
        if (document.Selection is null) throw new InvalidOperationException("Select an area to fill first.");
        var source = document.Layers.First(l => l.Id == layerId);
        if (source.IsGroup || source.IsAdjustment) throw new InvalidOperationException("Select image pixels for content-aware fill.");
        var coverage = SelectionCoverage.Create(document.Selection, document.Width, document.Height);
        if (coverage.IsEmpty) return document;
        using var path = SelectionGeometry.Path(document.Selection);
        var bounds = path.Bounds;
        double x0 = Math.Clamp(bounds.Left, 0, document.Width), x1 = Math.Clamp(bounds.Right, 0, document.Width);
        double y0 = Math.Clamp(bounds.Top, 0, document.Height), y1 = Math.Clamp(bounds.Bottom, 0, document.Height);
        if (x1 <= x0 || y1 <= y0) return document;
        PointD[] corners = [new(x0,y0),new(x1,y0),new(x0,y1),new(x1,y1)];
        var points = corners.Select(p => source.Transform.ToPixels(p, source.Pixels.Width, source.Pixels.Height)).ToArray();
        int left = checked((int)Math.Floor(Math.Min(0, points.Min(p => p.X)))), top = checked((int)Math.Floor(Math.Min(0, points.Min(p => p.Y))));
        int right = checked((int)Math.Ceiling(Math.Max(source.Pixels.Width, points.Max(p => p.X)))), bottom = checked((int)Math.Ceiling(Math.Max(source.Pixels.Height, points.Max(p => p.Y))));
        var grown = RasterReframe.Apply(source, new(left, top, checked(right-left), checked(bottom-top)), cancellation);
        // Validate total image/mask budgets before allocating the native work buffers.
        document.Replace(grown).Validate();
        var mask = new byte[checked(grown.Pixels.Width * grown.Pixels.Height)];
        for (int y = 0; y < grown.Pixels.Height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < grown.Pixels.Width; x++)
                mask[y * grown.Pixels.Width + x] = (byte)Math.Round(coverage.Sample(grown.Transform.ToDocument(new(x+.5,y+.5), grown.Pixels.Width, grown.Pixels.Height)) * 255, MidpointRounding.AwayFromZero);
        }
        var filled = Apply(grown.Pixels, mask, cancellation);
        var pixels = SelectionPixels.Blend(grown.Pixels, filled, grown.Transform, coverage, cancellation);
        if (ReferenceEquals(pixels, grown.Pixels)) return document;
        var result = document.Replace(grown with { Pixels = pixels }); result.Validate();
        if (EditorSession.UndoBytesRequired(document, result) > EditorSession.MaxHistoryBytes)
            throw new InvalidOperationException("Content-aware fill exceeds the 256 MiB Undo limit.");
        cancellation.ThrowIfCancellationRequested(); return result;
    }

    public static Raster Apply(Raster source, ReadOnlySpan<byte> selection, CancellationToken cancellation = default)
    {
        if (selection.Length != checked(source.Width * source.Height)) throw new ArgumentException("Selection dimensions must match the image.", nameof(selection));
        cancellation.ThrowIfCancellationRequested();
        if (selection.IndexOfAnyExcept((byte)0) < 0) return source;
        var mask = selection.ToArray();
        var input = source.ToRgba();
        cancellation.ThrowIfCancellationRequested();
        int result = NativePixels.ContentFill(input, mask, source.Width, source.Height);
        cancellation.ThrowIfCancellationRequested();
        if (result == 0) throw new InvalidOperationException("Not enough unselected, opaque image pixels to synthesize a fill. Use a smaller selection with surrounding image.");
        if (result != 1) throw new OutOfMemoryException("Content-aware fill could not allocate its working buffers.");
        var pixels = Raster.FromRgba(source.Width, source.Height, input);
        var tiles = pixels.Tiles.ToBuilder();
        bool changed = source.Tiles.Count != pixels.Tiles.Count;
        foreach (var (key, tile) in pixels.Tiles)
        {
            cancellation.ThrowIfCancellationRequested();
            if (source.Tiles.TryGetValue(key, out var original) && original.Bytes.SequenceEqual(tile.Bytes)) tiles[key] = original;
            else changed = true;
        }
        cancellation.ThrowIfCancellationRequested();
        return changed ? new Raster(source.Width, source.Height, tiles.ToImmutable()) : source;
    }
}
