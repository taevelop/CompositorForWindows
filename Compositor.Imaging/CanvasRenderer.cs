using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Shared screen/export renderer. Caches tiles; Adjustment documents also cache one canonical composite.</summary>
public sealed class CanvasRenderer : IDisposable
{
    private sealed record Cached(PixelTile?[] Neighbors, ColorOverlayEffect? Overlay, SKImage Image);
    private readonly Dictionary<(Guid, TileKey), Cached> cache = [];
    private readonly MaskPlacementCache maskPlacements = new();
    internal LayerMask ResolvePlacedMask(LayerMask mask,LayerTransform placement,LayerTransform layer,int width,int height,
        CancellationToken cancellationToken=default)=>maskPlacements.Resolve(mask,placement,layer,width,height,cancellationToken);
    internal int TileImageBuildCount {get;private set;}
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
    public void Draw(SKCanvas canvas,Document document)=>DrawCancellable(canvas,document,CancellationToken.None);
    internal void DrawCancellable(SKCanvas canvas,Document document,CancellationToken cancellationToken)
    {
        int count=canvas.SaveCount;
        try{cancellationToken.ThrowIfCancellationRequested();DrawCore(canvas,document,cancellationToken);}
        finally{canvas.RestoreToCount(count);}
    }
    private void DrawCore(SKCanvas canvas, Document document,CancellationToken cancellationToken)
    {
        if (!document.Layers.Any(l => HasSurfaceEffects(l))) ClearShadowSource();
        if (document.Layers.Any(l => l.IsAdjustment))
        {
            if (!ReferenceEquals(compositeDocument, document))
            {
                using var bitmap = new SKBitmap(Info(document.Width, document.Height));
                using (var target = new SKCanvas(bitmap)) { target.Clear(); DrawLayers(target, document, bitmap,cancellationToken); target.Flush(); }
                bitmap.SetImmutable();
                var next = SKImage.FromBitmap(bitmap);
                composite?.Dispose(); composite = next; compositeDocument = document;
            }
            canvas.DrawImage(composite!, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest));
            return;
        }
        composite?.Dispose(); composite = null; compositeDocument = null;
        DrawLayers(canvas, document,null,cancellationToken);
    }
    private void DrawLayers(SKCanvas canvas, Document document, SKBitmap? adjustmentSurface,CancellationToken cancellationToken)
    {
        var used = new HashSet<(Guid, TileKey)>();
        canvas.Save(); canvas.ClipRect(new(0, 0, document.Width, document.Height));
        foreach (var entry in LayerHierarchy.Entries(document))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var layer = entry.Layer;
            if (layer.IsGroup || !entry.Visible || entry.Opacity <= 0) continue;
            if(layer.Mask is { Enabled:true, Placement:{} placement } placedMask)
                layer=layer with { Mask=ResolvePlacedMask(placedMask,placement,layer.Transform,layer.Pixels.Width,layer.Pixels.Height,cancellationToken) };
            if (layer.IsAdjustment)
            {
                canvas.Flush(); AdjustmentProcessor.Apply(adjustmentSurface!, document, layer, entry.Opacity,cancellationToken); continue;
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
            if (HasSurfaceEffects(layer))
            {
                DrawShadowLayer(canvas, layer, paint, sampling);
                canvas.Restore(); continue;
            }
            var overlay = layer.Effects?.ColorOverlay is { IsEnabled: true, Opacity: > 0 } effect ? effect : null;
            byte[]? overlayTable = overlay is null ? null : OverlayTable(overlay);
            foreach (var (key, tile) in layer.Pixels.Tiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
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
                    cached = new(neighbors, overlay, TileImage(layer.Pixels, key, mask, overlayTable)); cache[cacheKey] = cached;TileImageBuildCount++;
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

    internal static bool HasSurfaceEffects(Layer layer) => layer.Effects?.Shadow is { IsEnabled: true, Opacity: > 0 } ||
        layer.Effects?.Stroke is { IsEnabled: true, Opacity: > 0, Size: > 0 } ||
        layer.Effects?.InnerShadow is { IsEnabled: true, Opacity: > 0 } || layer.Effects?.OuterGlow is { IsEnabled: true, Opacity: > 0 };
    private SKImage? innerImage, glowImage;
    private ShadowEffect? cachedInner;
    private OuterGlowEffect? cachedGlow;
    private int glowInset;
    private SKImage? strokeImage;
    private StrokeEffect? cachedStroke;
    private int strokeInset;
    private Raster? shadowPixels, shadowMask;
    private ColorOverlayEffect? shadowOverlay;
    private SKImage? shadowSource, shadowColored;
    private void ClearShadowSource()
    {
        innerImage?.Dispose(); glowImage?.Dispose(); innerImage = glowImage = null; cachedInner = null; cachedGlow = null;
        strokeImage?.Dispose(); strokeImage = null; cachedStroke = null;
        shadowSource?.Dispose(); shadowColored?.Dispose();
        shadowSource = shadowColored = null; shadowPixels = shadowMask = null; shadowOverlay = null;
    }
    private void DrawShadowLayer(SKCanvas canvas, Layer layer, SKPaint layerPaint, SKSamplingOptions sampling)
    {
        // A single source image avoids blur seams at tile boundaries. The filter expands beyond
        // the source rectangle; only the document clip limits the result.
        var currentMask = layer.Mask is { Enabled: true } activeMask ? activeMask.Pixels : null;
        var currentOverlay = layer.Effects?.ColorOverlay is { IsEnabled: true, Opacity: > 0 } activeOverlay ? activeOverlay : null;
        if (shadowSource is null || !ReferenceEquals(shadowPixels, layer.Pixels) || !ReferenceEquals(shadowMask, currentMask) || shadowOverlay != currentOverlay)
        {
            ClearShadowSource();
            using var bitmap = new SKBitmap(Info(layer.Pixels.Width, layer.Pixels.Height));
            var bytes = bitmap.GetPixelSpan();
            bytes.Clear();
            foreach (var (key, tile) in layer.Pixels.Tiles)
            for (int row = 0; row < Math.Min(256, bitmap.Height - key.Y * 256); row++)
                tile.Bytes.Slice(row * 1024, Math.Min(256, bitmap.Width - key.X * 256) * 4)
                    .CopyTo(bytes.Slice(((key.Y * 256 + row) * bitmap.Width + key.X * 256) * 4));
            if (layer.Mask is { Enabled: true } mask)
            {
                if (mask.Pixels.Width == 1 && mask.Pixels.Height == 1)
                {
                    int alpha = mask.Pixels.Tiles[new(0, 0)].Bytes[0];
                    if (alpha != 255) for (int p = 0; p < bytes.Length; p++) bytes[p] = (byte)((bytes[p] * alpha + 127) / 255);
                }
                else
                {
                    foreach (var (key, tile) in mask.Pixels.Tiles)
                    for (int row = 0; row < Math.Min(256, bitmap.Height - key.Y * 256); row++)
                    for (int x = 0; x < Math.Min(256, bitmap.Width - key.X * 256); x++)
                    {
                        int alpha = tile.Bytes[(row * 256 + x) * 4];
                        int p = ((key.Y * 256 + row) * bitmap.Width + key.X * 256 + x) * 4;
                        for (int c = 0; c < 4; c++) bytes[p + c] = (byte)((bytes[p + c] * alpha + 127) / 255);
                    }
                }
            }
            shadowSource = SKImage.FromBitmap(bitmap);
            if (currentOverlay is not null)
            {
                ColorOverlayProcessor.ApplyLookup(bytes, OverlayTable(currentOverlay));
                shadowColored = SKImage.FromBitmap(bitmap);
            }
            shadowPixels = layer.Pixels; shadowMask = currentMask; shadowOverlay = currentOverlay;
        }
        var stroke = layer.Effects?.Stroke is { IsEnabled: true, Opacity: > 0, Size: > 0 } st ? st : null;
        if (cachedStroke != stroke)
        {
            strokeImage?.Dispose(); strokeImage = null; cachedStroke = null;
            if (stroke is not null) strokeImage = StrokeProcessor.Render(shadowSource!, stroke, out strokeInset);
            cachedStroke = stroke;
        }
        var inner = layer.Effects?.InnerShadow is { IsEnabled: true, Opacity: > 0 } i ? i : null;
        var glow = layer.Effects?.OuterGlow is { IsEnabled: true, Opacity: > 0 } g ? g : null;
        if (inner != cachedInner)
        {
            innerImage?.Dispose(); innerImage = null; cachedInner = null;
            if (inner is not null) innerImage = SoftEffectProcessor.Render(shadowSource!, inner.OffsetX, inner.OffsetY, inner.Blur,
                inner.Red, inner.Green, inner.Blue, inner.Opacity, true, out _);
            cachedInner = inner;
        }
        if (glow != cachedGlow)
        {
            glowImage?.Dispose(); glowImage = null; cachedGlow = null;
            if (glow is not null) glowImage = SoftEffectProcessor.Render(shadowSource!, 0, 0, glow.Size,
                glow.Red, glow.Green, glow.Blue, glow.Opacity, false, out glowInset);
            cachedGlow = glow;
        }
        canvas.SaveLayer(layerPaint);
        if (layer.Effects?.Shadow is { IsEnabled: true, Opacity: > 0 } shadow)
        {
        using var filter = SKImageFilter.CreateDropShadowOnly((float)shadow.OffsetX, (float)shadow.OffsetY,
            (float)(shadow.Blur / 2), (float)(shadow.Blur / 2),
            new SKColor((byte)Math.Round(shadow.Red * 255), (byte)Math.Round(shadow.Green * 255),
                (byte)Math.Round(shadow.Blue * 255), (byte)Math.Round(shadow.Opacity * 255)));
        using var shadowPaint = new SKPaint { ImageFilter = filter };
        // Composite source + shadow first, then apply layer blend/opacity exactly once.
        canvas.DrawImage(shadowSource!, 0, 0, sampling, shadowPaint);
        }
        if (glowImage is not null) canvas.DrawImage(glowImage, -glowInset, -glowInset, sampling);
        if (stroke is { Inside: false }) canvas.DrawImage(strokeImage!, -strokeInset, -strokeInset, sampling);
        canvas.DrawImage(shadowColored ?? shadowSource!, 0, 0, sampling);
        if (innerImage is not null) canvas.DrawImage(innerImage, 0, 0, sampling);
        if (stroke is { Inside: true }) canvas.DrawImage(strokeImage!, -strokeInset, -strokeInset, sampling);
        canvas.Restore();
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
    public void Dispose() { ClearShadowSource(); maskPlacements.Clear(); overlayTables.Clear(); composite?.Dispose(); composite = null; compositeDocument = null; foreach (var item in cache.Values) item.Image.Dispose(); cache.Clear(); }
}
