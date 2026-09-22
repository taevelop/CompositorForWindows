using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Persistent viewport surface. Pixel edits repaint only damaged tiles; structural edits repaint all.</summary>
public sealed class ViewportRenderer : IDisposable
{
    private readonly CanvasRenderer renderer = new();
    private SKSurface? surface;
    private Document? previous;
    private int width, height;
    private float zoom, offsetX, offsetY;

    public SKImage Render(Document document, int width, int height, float zoom, float offsetX, float offsetY)
    {
        if (width < 1 || height < 1 || !float.IsFinite(zoom) || zoom <= 0) throw new ArgumentException("Invalid viewport.");
        bool full = surface is null || this.width != width || this.height != height || this.zoom != zoom || this.offsetX != offsetX || this.offsetY != offsetY;
        if (surface is null || this.width != width || this.height != height)
        { surface?.Dispose(); surface = SKSurface.Create(CanvasRenderer.Info(width, height)) ?? throw new IOException("Cannot allocate viewport."); }
        var damage = full ? new SKRect(0, 0, width, height) : Damage(previous, document, zoom, offsetX, offsetY, width, height);
        this.width = width; this.height = height; this.zoom = zoom; this.offsetX = offsetX; this.offsetY = offsetY;
        if (!damage.IsEmpty)
        {
            var canvas = surface.Canvas;
            canvas.Save(); canvas.ClipRect(damage, SKClipOperation.Intersect, false);
            canvas.Clear(SKColors.Transparent);
            canvas.Translate(offsetX, offsetY); canvas.Scale(zoom);
            renderer.Draw(canvas, document); canvas.Restore();
        }
        previous = document;
        return surface.Snapshot();
    }

    private static SKRect Damage(Document? old, Document current, float zoom, float dx, float dy, int width, int height)
    {
        var full = new SKRect(0, 0, width, height);
        if (old is null || old.Id != current.Id || old.Width != current.Width || old.Height != current.Height || old.Layers.Length != current.Layers.Length) return full;
        float left = width, top = height, right = 0, bottom = 0;
        for (int i = 0; i < current.Layers.Length; i++)
        {
            var a = old.Layers[i]; var b = current.Layers[i];
            if (a.Id != b.Id || a.Transform != b.Transform || a.Opacity != b.Opacity || a.Visible != b.Visible || a.Blend != b.Blend ||
                a.Pixels.Width != b.Pixels.Width || a.Pixels.Height != b.Pixels.Height) return full;
            if (!b.Visible || ReferenceEquals(a.Pixels, b.Pixels)) continue;
            foreach (var key in a.Pixels.Tiles.Keys.Union(b.Pixels.Tiles.Keys))
            {
                if (ReferenceEquals(a.Pixels.Tiles.GetValueOrDefault(key), b.Pixels.Tiles.GetValueOrDefault(key))) continue;
                // Include the neighboring sampling gutter and round outward in device pixels.
                PointD[] corners = [new(key.X * 256 - 2, key.Y * 256 - 2), new(key.X * 256 + 258, key.Y * 256 - 2),
                    new(key.X * 256 - 2, key.Y * 256 + 258), new(key.X * 256 + 258, key.Y * 256 + 258)];
                foreach (var corner in corners)
                {
                    var p = b.Transform.ToDocument(corner, b.Pixels.Width, b.Pixels.Height);
                    left = Math.Min(left, (float)Math.Floor(p.X * zoom + dx - 1)); top = Math.Min(top, (float)Math.Floor(p.Y * zoom + dy - 1));
                    right = Math.Max(right, (float)Math.Ceiling(p.X * zoom + dx + 1)); bottom = Math.Max(bottom, (float)Math.Ceiling(p.Y * zoom + dy + 1));
                }
            }
        }
        return right <= left || bottom <= top ? SKRect.Empty : SKRect.Intersect(new(left, top, right, bottom), full);
    }
    public void Dispose() { surface?.Dispose(); surface = null; previous = null; renderer.Dispose(); }
}
