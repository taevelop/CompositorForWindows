using System.Runtime.InteropServices;
using Compositor.Core;

namespace Compositor.Imaging;

public sealed record BrushSettings(double Diameter, double Hardness, double Opacity, byte Red, byte Green, byte Blue, bool Erase = false);
public static class NativePixels
{
    [DllImport("Compositor.Native", EntryPoint = "compositor_abi_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern int AbiVersion();
    [DllImport("Compositor.Native", EntryPoint = "compositor_alpha_bounds", CallingConvention = CallingConvention.Cdecl)]
    public static extern void AlphaBounds(byte[] pixels, int width, int height, int stride, [Out] int[] bounds);
    [DllImport("Compositor.Native", EntryPoint = "compositor_clamp", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Clamp([In, Out] byte[] pixels, int count);
    [DllImport("Compositor.Native", EntryPoint = "compositor_brush", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Brush([In, Out] byte[] output, byte[] original, [In, Out] float[] coverage,
        int tileX, int tileY, int width, int height, double[] map, double ax, double ay, double bx, double by,
        double radius, double hardness, double opacity, int red, int green, int blue, int erase, int canvasW, int canvasH);
}
public sealed class BrushStroke
{
    private sealed record Work(byte[] Original, byte[] Output, float[] Coverage);
    private readonly Dictionary<TileKey, Work> work = [];
    private readonly Layer layer;
    private readonly BrushSettings settings;
    private readonly int canvasWidth, canvasHeight;
    private readonly double[] mapping;
    private PointD? previous;
    public Raster Pixels { get; private set; }
    public BrushStroke(Layer layer, BrushSettings settings, int canvasWidth, int canvasHeight)
    {
        if (!double.IsFinite(settings.Diameter) || settings.Diameter is < 1 or > 2000 ||
            !double.IsFinite(settings.Hardness) || settings.Hardness is < 0 or > 1 ||
            !double.IsFinite(settings.Opacity) || settings.Opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(settings));
        if (layer.IsGroup) throw new InvalidOperationException("Groups cannot be painted directly.");
        this.layer = layer; this.settings = settings; this.canvasWidth = canvasWidth; this.canvasHeight = canvasHeight;
        Pixels = layer.Pixels;
        PointD p = layer.Transform.ToDocument(new(0, 0), Pixels.Width, Pixels.Height),
            x = layer.Transform.ToDocument(new(1, 0), Pixels.Width, Pixels.Height),
            y = layer.Transform.ToDocument(new(0, 1), Pixels.Width, Pixels.Height);
        mapping = [x.X - p.X, x.Y - p.Y, y.X - p.X, y.Y - p.Y, p.X, p.Y];
    }
    public void Append(PointD point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentException("Invalid pointer.");
        var start = previous ?? point; previous = point;
        double radius = settings.Diameter / 2;
        double left = Math.Min(start.X, point.X) - radius, right = Math.Max(start.X, point.X) + radius;
        double top = Math.Min(start.Y, point.Y) - radius, bottom = Math.Max(start.Y, point.Y) + radius;
        PointD[] corners = [new(left, top), new(right, top), new(left, bottom), new(right, bottom)];
        var local = corners.Select(p => layer.Transform.ToPixels(p, Pixels.Width, Pixels.Height)).ToArray();
        int minX = (int)Math.Clamp(Math.Floor(local.Min(p => p.X) / 256), 0, (Pixels.Width - 1) / 256);
        int maxX = (int)Math.Clamp(Math.Floor(local.Max(p => p.X) / 256), 0, (Pixels.Width - 1) / 256);
        int minY = (int)Math.Clamp(Math.Floor(local.Min(p => p.Y) / 256), 0, (Pixels.Height - 1) / 256);
        int maxY = (int)Math.Clamp(Math.Floor(local.Max(p => p.Y) / 256), 0, (Pixels.Height - 1) / 256);
        var tiles = Pixels.Tiles.ToBuilder();
        bool changed = false;
        for (int ty = minY; ty <= maxY; ty++)
        for (int tx = minX; tx <= maxX; tx++)
        {
            var key = new TileKey(tx, ty);
            if (!work.TryGetValue(key, out var tile))
            {
                byte[] original = layer.Pixels.Tiles.TryGetValue(key, out var source) ? source.Bytes.ToArray() : new byte[PixelTile.ByteCount];
                tile = new(original, (byte[])original.Clone(), new float[256 * 256]); work.Add(key, tile);
            }
            int updated = NativePixels.Brush(tile.Output, tile.Original, tile.Coverage, tx * 256, ty * 256,
                Math.Min(256, Pixels.Width - tx * 256), Math.Min(256, Pixels.Height - ty * 256), mapping,
                start.X, start.Y, point.X, point.Y, radius, settings.Hardness, settings.Opacity,
                settings.Red, settings.Green, settings.Blue, settings.Erase ? 1 : 0, canvasWidth, canvasHeight);
            if (updated == 0) continue;
            changed = true;
            if (tile.Output.AsSpan().IndexOfAnyExcept((byte)0) < 0) tiles.Remove(key);
            else tiles[key] = new(tile.Output);
        }
        if (changed) Pixels = new(Pixels.Width, Pixels.Height, tiles.ToImmutable());
    }
}
