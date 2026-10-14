using Compositor.Core;

namespace Compositor.Imaging;

/// <summary>Copies immutable tiles into an existing packed RGBA buffer without a full-image intermediate.</summary>
internal static class RasterBuffer
{
    internal static void CopyTo(Raster source,Span<byte> destination,CancellationToken cancellation=default)
    {
        if(destination.Length!=checked(source.Width*source.Height*4))throw new ArgumentException("Destination must be packed RGBA with matching dimensions.",nameof(destination));
        cancellation.ThrowIfCancellationRequested();destination.Clear();
        foreach(var(key,tile) in source.Tiles)
        {
            cancellation.ThrowIfCancellationRequested();
            int width=Math.Min(256,source.Width-key.X*256),height=Math.Min(256,source.Height-key.Y*256);
            for(int y=0;y<height;y++)
            {
                cancellation.ThrowIfCancellationRequested();
                tile.Bytes.Slice(y*PixelTile.Stride,width*4).CopyTo(destination.Slice(((key.Y*256+y)*source.Width+key.X*256)*4,width*4));
            }
        }
    }
}
