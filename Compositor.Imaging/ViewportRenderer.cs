using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Persistent viewport surface. Pixel edits repaint only damaged tiles; structural edits repaint all.</summary>
public sealed class ViewportRenderer : IDisposable
{
    private bool disposed;
    private CanvasRenderer renderer = new();
    internal int TileImageBuildCount=>renderer.TileImageBuildCount;
    private CompositeColorSampler sampler;
    public ViewportRenderer(){sampler=new(renderer);}
    // UI-thread confined: shares canonical document caches, never samples viewport pixels.
    public SampledColor? Sample(Document document,PointD point)=>sampler.Sample(document,point);
    public void InstallPrepared(PreparedDocumentRender prepared,Document expected)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        var next=prepared.Take(expected);
        sampler.Dispose();renderer.Dispose();renderer=next;sampler=new(renderer);
        // Force repaint even if dimensions and document identity match a prior frame.
        previous=null;
    }
    private SKSurface? surface;
    private Document? previous;
    private int width, height;
    private float zoom, offsetX, offsetY;
    public void InvalidatePreviousFrame()=>previous=null;
    /// <summary>Display-only last completed pixels while a replacement is prepared.</summary>
    public SKImage? PreviousFrame(Document document,int width,int height,float zoom,float offsetX,float offsetY)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
        if(surface is null||previous is null||previous.Id!=document.Id||previous.Width!=document.Width||previous.Height!=document.Height||
            this.width!=width||this.height!=height||this.zoom!=zoom||this.offsetX!=offsetX||this.offsetY!=offsetY)return null;
        return surface.Snapshot();
    }

    public SKImage Render(Document document, int width, int height, float zoom, float offsetX, float offsetY)
    {
        ObjectDisposedException.ThrowIf(disposed,this);
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
        if (!ReferenceEquals(old, current) && (old.Layers.Any(l => l.IsAdjustment || CanvasRenderer.HasSurfaceEffects(l)) || current.Layers.Any(l => l.IsAdjustment || CanvasRenderer.HasSurfaceEffects(l)))) return full;
        float left = width, top = height, right = 0, bottom = 0;
        for (int i = 0; i < current.Layers.Length; i++)
        {
            var a = old.Layers[i]; var b = current.Layers[i];
            if (a.Id != b.Id || a.ParentId != b.ParentId || a.IsGroup != b.IsGroup || a.Transform != b.Transform || a.Opacity != b.Opacity || a.Visible != b.Visible || a.Blend != b.Blend || a.Effects != b.Effects ||
                a.Pixels.Width != b.Pixels.Width || a.Pixels.Height != b.Pixels.Height) return full;
            var am = a.Mask is { Enabled: true } aMask ? aMask.Pixels : null;
            var bm = b.Mask is { Enabled: true } bMask ? bMask.Pixels : null;
            if(a.Mask?.Placement!=b.Mask?.Placement)return full;
            if(b.Mask?.Placement is not null && !ReferenceEquals(am,bm))return full;
            if (am?.Width != bm?.Width || am?.Height != bm?.Height) return full;
            if (bm?.Width == 1 && bm.Height == 1 && !ReferenceEquals(am, bm)) return full;
            if (!b.Visible || (ReferenceEquals(a.Pixels, b.Pixels) && ReferenceEquals(am, bm))) continue;
            // Skia's rotated tile clips can quantize differently when intersected with a damage clip.
            // Repaint the whole viewport for rotated pixel edits to keep export and display identical.
            if (b.Transform.Rotation % 90 != 0) return full;
            foreach (var key in a.Pixels.Tiles.Keys.Union(b.Pixels.Tiles.Keys).Union(am?.Tiles.Keys ?? Enumerable.Empty<TileKey>()).Union(bm?.Tiles.Keys ?? Enumerable.Empty<TileKey>()))
            {
                if (ReferenceEquals(a.Pixels.Tiles.GetValueOrDefault(key), b.Pixels.Tiles.GetValueOrDefault(key)) &&
                    ReferenceEquals(am?.Tiles.GetValueOrDefault(key), bm?.Tiles.GetValueOrDefault(key))) continue;
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
        if (right <= left || bottom <= top) return SKRect.Empty;
        // Unchanged rotated overlays also pass through the damage clip during recomposition.
        if (current.Layers.Any(l => !l.IsGroup && l.Visible && l.Opacity > 0 && l.Transform.Rotation % 90 != 0)) return full;
        return SKRect.Intersect(new(left, top, right, bottom), full);
    }
    public void Dispose() { if(disposed)return;disposed=true;sampler.Dispose(); surface?.Dispose(); surface = null; previous = null; renderer.Dispose(); }
}
