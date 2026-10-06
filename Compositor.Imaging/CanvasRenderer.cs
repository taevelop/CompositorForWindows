using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Shared screen/export renderer. Caches tiles; Adjustment documents also cache one canonical composite.</summary>
public sealed class CanvasRenderer : IDisposable
{
    private sealed record Cached(PixelTile?[] Neighbors, ColorOverlayEffect? Overlay, SKImage Image);
    private readonly Dictionary<(Guid, TileKey), Cached> cache = [];
    // Bounded independently of document layer count; tile images only retain the effect values.
    private readonly Dictionary<ColorOverlayEffect, byte[]> overlayTables = [];
    private byte[] OverlayTable(ColorOverlayEffect effect)
    {
        if (overlayTables.TryGetValue(effect, out var table)) return table;
        if (overlayTables.Count >= 8) overlayTables.Clear();
        return overlayTables[effect] = ColorOverlayProcessor.Lookup(effect);
    }
    private static readonly SKColorSpace WorkingColorSpace = SKColorSpace.CreateSrgb();
    public static SKImageInfo Info(int width, int height) =>
        new(width, height, SKColorType.Rgba8888, SKAlphaType.Premul, WorkingColorSpace);

    private Document? compositeDocument;
    private SKImage? composite;
    public void Draw(SKCanvas canvas, Document document)
    {
        if (document.Layers.Any(l => l.IsAdjustment))
        {
            if (!ReferenceEquals(compositeDocument, document))
            {
                using var bitmap = new SKBitmap(Info(document.Width, document.Height));
                using (var target = new SKCanvas(bitmap)) { target.Clear(); DrawLayers(target, document, bitmap); target.Flush(); }
                bitmap.SetImmutable();
                var next = SKImage.FromBitmap(bitmap);
                composite?.Dispose(); composite = next; compositeDocument = document;
            }
            canvas.DrawImage(composite!, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            return;
        }
        composite?.Dispose(); composite = null; compositeDocument = null;
        DrawLayers(canvas, document);
    }
    private void DrawLayers(SKCanvas canvas, Document document, SKBitmap? adjustmentSurface = null)
    {
        var used = new HashSet<(Guid, TileKey)>();
        canvas.Save(); canvas.ClipRect(new(0, 0, document.Width, document.Height));
        foreach (var entry in LayerHierarchy.Entries(document))
        {
            var layer = entry.Layer;
            if (layer.IsGroup || !entry.Visible || entry.Opacity <= 0) continue;
            if (layer.IsAdjustment)
            {
                canvas.Flush(); AdjustmentProcessor.Apply(adjustmentSurface!, document, layer, entry.Opacity); continue;
            }
            var t = layer.Transform;
            canvas.Save();
            canvas.Translate((float)(t.X + t.Width / 2), (float)(t.Y + t.Height / 2));
            canvas.RotateDegrees((float)t.Rotation);
            canvas.Scale((float)(t.Width / layer.Pixels.Width * (t.FlipX ? -1 : 1)),
                (float)(t.Height / layer.Pixels.Height * (t.FlipY ? -1 : 1)));
            canvas.Translate(-layer.Pixels.Width / 2f, -layer.Pixels.Height / 2f);
            using var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(entry.Opacity * 255)),
                BlendMode = Blend(layer.Blend), IsAntialias = false };
            var sampling = new SKSamplingOptions(t.Sampling == Sampling.Nearest ? SKFilterMode.Nearest : SKFilterMode.Linear);
            var overlay = layer.Effects?.ColorOverlay is { IsEnabled: true, Opacity: > 0 } effect ? effect : null;
            byte[]? overlayTable = overlay is null ? null : OverlayTable(overlay);
            foreach (var (key, tile) in layer.Pixels.Tiles)
            {
                float x = key.X * 256, y = key.Y * 256;
                var bounds = new SKRect(x, y, Math.Min(x + 256, layer.Pixels.Width), Math.Min(y + 256, layer.Pixels.Height));
                var cacheKey = (layer.Id, key); used.Add(cacheKey);
                if (!canvas.LocalClipBounds.IntersectsWith(bounds)) continue;
                var mask = layer.Mask is { Enabled: true } m ? m.Pixels : null;
                var neighbors = new PixelTile?[mask is null ? 9 : 18]; int index = 0;
                for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                    neighbors[index++] = layer.Pixels.Tiles.GetValueOrDefault(new(key.X + dx, key.Y + dy));
                if (mask is not null)
                    for (int dy = -1; dy <= 1; dy++) for (int dx = -1; dx <= 1; dx++)
                        neighbors[index++] = mask.Tiles.GetValueOrDefault(mask.Width == 1 && mask.Height == 1 ? new(0, 0) : new(key.X + dx, key.Y + dy));
                if (!cache.TryGetValue(cacheKey, out var cached) || cached.Overlay != overlay || !cached.Neighbors.SequenceEqual(neighbors))
                {
                    cached?.Image.Dispose();
                    cached = new(neighbors, overlay, TileImage(layer.Pixels, key, mask, overlayTable)); cache[cacheKey] = cached;
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

    private static unsafe SKImage TileImage(Raster raster, TileKey key, Raster? mask, byte[]? overlayTable)
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
        if (mask is not null)
        {
            if (mask.Width == 1 && mask.Height == 1)
            {
                byte coverage = mask.Tiles[new(0, 0)].Bytes[0];
                if (coverage != 255)
                    for (int i = 0; i < bytes.Length; i++) bytes[i] = (byte)((bytes[i] * coverage + 127) / 255);
            }
            else
            {
                for (int y = 0; y < 258; y++)
                {
                    int sy = Math.Clamp(key.Y * 256 + y - 1, 0, mask.Height - 1), x = 0;
                    while (x < 258)
                    {
                        int rawX = key.X * 256 + x - 1, sx = Math.Clamp(rawX, 0, mask.Width - 1);
                        int count = rawX < 0 || rawX >= mask.Width ? 1 : Math.Min(258 - x, Math.Min(256 - sx % 256, mask.Width - sx));
                        // Resolve immutable tile once per row segment, not once per pixel.
                        var coverage = mask.Tiles[new(sx / 256, sy / 256)].Bytes.Slice(((sy % 256) * 256 + sx % 256) * 4, count * 4);
                        var destination = bytes.Slice((y * 258 + x) * 4, count * 4);
                        for (int p = 0; p < destination.Length; p += 4)
                        {
                            int value = coverage[p];
                            if (value == 255) continue;
                            destination[p] = (byte)((destination[p] * value + 127) / 255);
                            destination[p + 1] = (byte)((destination[p + 1] * value + 127) / 255);
                            destination[p + 2] = (byte)((destination[p + 2] * value + 127) / 255);
                            destination[p + 3] = (byte)((destination[p + 3] * value + 127) / 255);
                        }
                        x += count;
                    }
                }
            }
        }
        if (overlayTable is not null) ColorOverlayProcessor.ApplyLookup(bytes, overlayTable);
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
    public void Dispose() { overlayTables.Clear(); composite?.Dispose(); composite = null; compositeDocument = null; foreach (var item in cache.Values) item.Image.Dispose(); cache.Clear(); }
}
