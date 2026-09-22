using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Shared by the screen and export. Caches tile images, never a flattened document.</summary>
public sealed class CanvasRenderer : IDisposable
{
    private sealed record Cached(PixelTile?[] Neighbors, SKImage Image);
    private readonly Dictionary<(Guid, TileKey), Cached> cache = [];
    private static readonly SKColorSpace WorkingColorSpace = SKColorSpace.CreateSrgb();
    public static SKImageInfo Info(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, WorkingColorSpace);

    public void Draw(SKCanvas canvas, Document document)
    {
        var used = new HashSet<(Guid, TileKey)>();
        canvas.Save(); canvas.ClipRect(new(0, 0, document.Width, document.Height));
        foreach (var layer in document.Layers)
        {
            if (!layer.Visible || layer.Opacity <= 0) continue;
            var t = layer.Transform;
            canvas.Save();
            canvas.Translate((float)(t.X + t.Width / 2), (float)(t.Y + t.Height / 2));
            canvas.RotateDegrees((float)t.Rotation);
            canvas.Scale((float)(t.Width / layer.Pixels.Width * (t.FlipX ? -1 : 1)),
                (float)(t.Height / layer.Pixels.Height * (t.FlipY ? -1 : 1)));
            canvas.Translate(-layer.Pixels.Width / 2f, -layer.Pixels.Height / 2f);
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(layer.Opacity * 255)),
                BlendMode = Blend(layer.Blend), IsAntialias = false };
            var sampling = new SKSamplingOptions(t.Sampling == Sampling.Nearest ? SKFilterMode.Nearest : SKFilterMode.Linear);
            foreach (var (key, tile) in layer.Pixels.Tiles)
            {
                float x = key.X * 256, y = key.Y * 256;
                var bounds = new SKRect(x, y, Math.Min(x + 256, layer.Pixels.Width), Math.Min(y + 256, layer.Pixels.Height));
                var cacheKey = (layer.Id, key); used.Add(cacheKey);
                if (!canvas.LocalClipBounds.IntersectsWith(bounds)) continue;
                var neighbors = new PixelTile?[9]; int index = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    neighbors[index++] = layer.Pixels.Tiles.GetValueOrDefault(new(key.X + dx, key.Y + dy));
                if (!cache.TryGetValue(cacheKey, out var cached) || !cached.Neighbors.SequenceEqual(neighbors))
                {
                    cached?.Image.Dispose();
                    cached = new(neighbors, TileImage(layer.Pixels, key)); cache[cacheKey] = cached;
                }
                canvas.Save(); canvas.ClipRect(bounds, SKClipOperation.Intersect, false);
                canvas.DrawImage(cached.Image, new SKRect(x - 1, y - 1, x + 257, y + 257), sampling, paint);
                canvas.Restore();
            }
            canvas.Restore();
        }
        canvas.Restore();
        foreach (var key in cache.Keys.Where(k => !used.Contains(k)).ToArray()) { cache[key].Image.Dispose(); cache.Remove(key); }
    }

    private static unsafe SKImage TileImage(Raster raster, TileKey key)
    {
        // One-pixel neighboring gutters prevent interpolation seams between tiles.
        using var bitmap = new SKBitmap(Info(258, 258));
        var bytes = bitmap.GetPixelSpan();
        bytes.Clear();
        for (int y = 0; y < 258; y++)
        {
            int sy = Math.Clamp(key.Y * 256 + y - 1, 0, raster.Height - 1);
            int x = 0;
            while (x < 258)
            {
                int rawX = key.X * 256 + x - 1;
                int sx = Math.Clamp(rawX, 0, raster.Width - 1);
                int count = rawX < 0 || rawX >= raster.Width ? 1 : Math.Min(258 - x, Math.Min(256 - sx % 256, raster.Width - sx));
                if (raster.Tiles.TryGetValue(new(sx / 256, sy / 256), out var source))
                    source.Bytes.Slice(((sy % 256) * 256 + sx % 256) * 4, count * 4).CopyTo(bytes.Slice((y * 258 + x) * 4));
                x += count;
            }
        }
        bitmap.SetImmutable();
        return SKImage.FromBitmap(bitmap);
    }

    public SKImage Flatten(Document document, bool whiteBackground = false)
    {
        document.Validate();
        using var surface = SKSurface.Create(Info(document.Width, document.Height)) ?? throw new IOException("Cannot allocate export surface.");
        surface.Canvas.Clear(whiteBackground ? SKColors.White : SKColors.Transparent);
        Draw(surface.Canvas, document);
        return surface.Snapshot();
    }
    private static SKBlendMode Blend(BlendMode mode) => mode switch
    {
        BlendMode.Normal => SKBlendMode.SrcOver, BlendMode.Multiply => SKBlendMode.Multiply,
        BlendMode.Screen => SKBlendMode.Screen, BlendMode.Overlay => SKBlendMode.Overlay,
        BlendMode.Darken => SKBlendMode.Darken, BlendMode.Lighten => SKBlendMode.Lighten,
        BlendMode.Difference => SKBlendMode.Difference, _ => throw new NotSupportedException("Unsupported blend mode.")
    };
    public void Dispose() { foreach (var item in cache.Values) item.Image.Dispose(); cache.Clear(); }
}
