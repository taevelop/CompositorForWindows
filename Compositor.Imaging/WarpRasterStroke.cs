using Compositor.Core;

namespace Compositor.Imaging;

public enum SmearMode { Liquify, Blur, Smudge }

/// <summary>Original document-space Smudge/Liquify dabs on a private premultiplied working copy.</summary>
public sealed class WarpRasterStroke
{
    private readonly int width, height, radius;
    private readonly double diameter, hardness, strength;
    private readonly SmearMode mode;
    private readonly byte[] pixels;
    private float[] carried = [], scratch = [];
    private PointD? last;
    private bool faulted;
    internal bool IsFaulted => faulted;
    private readonly List<PointD> points = [];
    private readonly HashSet<TileKey> dirty = [];
    public IReadOnlyList<PointD> Points => points.AsReadOnly();
    public Raster Pixels { get; private set; }

    public WarpRasterStroke(Raster canvas, BrushSettings settings, SmearMode mode, CancellationToken cancellation = default)
    {
        if (mode is not (SmearMode.Smudge or SmearMode.Liquify) || !double.IsFinite(settings.Diameter) || settings.Diameter is < 1 or > 2000 ||
            !double.IsFinite(settings.Hardness) || settings.Hardness is < 0 or > 1 || !double.IsFinite(settings.Opacity) || settings.Opacity is < 0 or > 1 || settings.Erase)
            throw new ArgumentException("Invalid warp settings.");
        cancellation.ThrowIfCancellationRequested();width = canvas.Width;height = canvas.Height;
        this.mode = mode;diameter = Math.Max(2, settings.Diameter);hardness = Math.Clamp(settings.Hardness, 0, .98);strength = Math.Clamp(settings.Opacity, .01, 1);radius = (int)Math.Ceiling(diameter / 2);
        Pixels = canvas;pixels = new byte[checked(width * height * 4)];RasterBuffer.CopyTo(canvas, pixels, cancellation);
    }
    private float Weight(float u)
    {
        if (u >= 1) return 0;if (u <= (float)hardness) return 1;
        float t = (1 - u) / (1 - (float)hardness);return t * t * (3 - 2 * t);
    }
    private static int Rounded(double value) => checked((int)Math.Round(value, MidpointRounding.AwayFromZero));
    private static byte Channel(float value) => (byte)Math.Clamp(MathF.Round(value, MidpointRounding.AwayFromZero), 0, 255);

    public void Append(PointD point, CancellationToken cancellation = default)
    {
        if (faulted) throw new InvalidOperationException("Discard a cancelled or failed warp stroke.");
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y) || Math.Abs(point.X) > 10_000_000 || Math.Abs(point.Y) > 10_000_000) throw new ArgumentException("Invalid warp pointer.");
        try
        {
            cancellation.ThrowIfCancellationRequested();
            if (last is not { } from) { last = point;if (mode == SmearMode.Smudge) PickUp(point, cancellation);return; }
            double distance = double.Hypot(point.X - from.X, point.Y - from.Y), spacing = Math.Max(1, diameter * (mode == SmearMode.Smudge ? .08 : .025));
            if (distance < spacing) return;
            int steps = checked((int)Math.Ceiling(distance / spacing));var previous = from;
            for (int step = 1; step <= steps; step++)
            {
                cancellation.ThrowIfCancellationRequested();double t = (double)step / steps;
                var next = new PointD(from.X + (point.X - from.X) * t, from.Y + (point.Y - from.Y) * t);
                if (mode == SmearMode.Smudge) Smudge(next, cancellation);else Push(previous, next, cancellation);
                points.Add(next);previous = next;
            }
            Publish(cancellation);last = point;
        }
        catch { faulted = true;throw; }
    }
    private void Publish(CancellationToken cancellation)
    {
        var tiles = Pixels.Tiles.ToBuilder();bool changed = false;
        foreach (var key in dirty)
        {
            cancellation.ThrowIfCancellationRequested();int left = key.X * 256, top = key.Y * 256;
            int w = Math.Min(256, width - left), h = Math.Min(256, height - top);
            Pixels.Tiles.TryGetValue(key, out var before);var bytes = before is null ? new byte[PixelTile.ByteCount] : before.Bytes.ToArray();
            for (int y = 0; y < h; y++) pixels.AsSpan(((top + y) * width + left) * 4, w * 4).CopyTo(bytes.AsSpan(y * PixelTile.Stride));
            if (before is not null && before.Bytes.SequenceEqual(bytes)) continue;
            bool empty = bytes.AsSpan().IndexOfAnyExcept((byte)0) < 0;if (before is null && empty) continue;
            changed = true;if (empty) tiles.Remove(key);else tiles[key] = new(bytes);
        }
        cancellation.ThrowIfCancellationRequested();if (changed) Pixels = new(width, height, tiles.ToImmutable());dirty.Clear();
    }
    private void PickUp(PointD center, CancellationToken cancellation)
    {
        int side = radius * 2 + 1, cx = Rounded(center.X), cy = Rounded(center.Y);carried = new float[checked(side * side * 4)];
        for (int dy = -radius; dy <= radius; dy++)
        {
            cancellation.ThrowIfCancellationRequested();int y = cy + dy;if (y < 0 || y >= height) continue;
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = cx + dx;if (x < 0 || x >= width) continue;
                int p = (y * width + x) * 4, c = ((dy + radius) * side + dx + radius) * 4;
                for (int k = 0; k < 4; k++) carried[c + k] = pixels[p + k];
            }
        }
    }
    private void Smudge(PointD center, CancellationToken cancellation)
    {
        int side = radius * 2 + 1, cx = Rounded(center.X), cy = Rounded(center.Y);float keep = (float)strength, invR = 1 / (float)(diameter / 2);
        for (int dy = -radius; dy <= radius; dy++)
        {
            cancellation.ThrowIfCancellationRequested();int y = cy + dy;if (y < 0 || y >= height) continue;
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = cx + dx;if (x < 0 || x >= width) continue;float w = Weight(MathF.Sqrt(dx * dx + dy * dy) * invR);if (w <= 0) continue;
                int p = (y * width + x) * 4, c = ((dy + radius) * side + dx + radius) * 4;
                bool changed = false;
                for (int k = 0; k < 4; k++)
                { float under = pixels[p + k], painted = under + (carried[c + k] - under) * w;byte value = Channel(painted);changed |= pixels[p + k] != value;pixels[p + k] = value;carried[c + k] = painted + (carried[c + k] - painted) * keep; }
                if (changed) dirty.Add(new(x / 256, y / 256));
            }
        }
    }
    private void Push(PointD a, PointD b, CancellationToken cancellation)
    {
        float mx = (float)(b.X - a.X) * (float)strength, my = (float)(b.Y - a.Y) * (float)strength;
        int margin = (int)MathF.Ceiling(Math.Max(Math.Abs(mx), Math.Abs(my))) + 2, cx = Rounded(b.X), cy = Rounded(b.Y);
        int x0 = Math.Max(0, cx - radius - margin), x1 = Math.Min(width - 1, cx + radius + margin), y0 = Math.Max(0, cy - radius - margin), y1 = Math.Min(height - 1, cy + radius + margin);
        if (x0 > x1 || y0 > y1) return;int cw = x1 - x0 + 1, ch = y1 - y0 + 1;
        if (scratch.Length < cw * ch * 4) scratch = new float[cw * ch * 4];
        for (int y = 0; y < ch; y++)
        { cancellation.ThrowIfCancellationRequested();for (int x = 0; x < cw; x++) for (int k = 0; k < 4; k++) scratch[(y * cw + x) * 4 + k] = pixels[((y + y0) * width + x + x0) * 4 + k]; }
        float invR = 1 / (float)(diameter / 2);
        for (int dy = -radius; dy <= radius; dy++)
        {
            cancellation.ThrowIfCancellationRequested();int y = cy + dy;if (y < y0 || y > y1) continue;
            for (int dx = -radius; dx <= radius; dx++)
            {
                int x = cx + dx;if (x < x0 || x > x1) continue;float w = Weight(MathF.Sqrt(dx * dx + dy * dy) * invR);if (w <= 0) continue;
                float sx = Math.Clamp(x - x0 - mx * w, 0, cw - 1), sy = Math.Clamp(y - y0 - my * w, 0, ch - 1);
                int ix = Math.Min(cw - 2, (int)sx), iy = Math.Min(ch - 2, (int)sy);if (ix < 0 || iy < 0) continue;
                float fx = sx - ix, fy = sy - iy;int p = (y * width + x) * 4, s00 = (iy * cw + ix) * 4;
                bool changed = false;
                for (int k = 0; k < 4; k++)
                { float top = scratch[s00 + k] + (scratch[s00 + 4 + k] - scratch[s00 + k]) * fx, bottom = scratch[s00 + cw * 4 + k] + (scratch[s00 + cw * 4 + 4 + k] - scratch[s00 + cw * 4 + k]) * fx;byte value = Channel(top + (bottom - top) * fy);changed |= pixels[p + k] != value;pixels[p + k] = value; }
                if (changed) dirty.Add(new(x / 256, y / 256));
            }
        }
    }
}
