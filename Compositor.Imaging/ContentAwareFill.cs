using Compositor.Core;

namespace Compositor.Imaging;

public static class ContentAwareFill
{
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
        cancellation.ThrowIfCancellationRequested();
        return pixels;
    }
}
