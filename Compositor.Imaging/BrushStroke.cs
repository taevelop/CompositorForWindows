using System.Runtime.InteropServices;
using Compositor.Core;

namespace Compositor.Imaging;

public sealed record BrushSettings(double Diameter, double Hardness, double Opacity, byte Red, byte Green, byte Blue, bool Erase = false);
public static class NativePixels
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int HealingCancellation(int phase, IntPtr context);
    [DllImport("Compositor.Native", EntryPoint="compositor_spot_heal_cancellable", CallingConvention=CallingConvention.Cdecl)]
    internal static extern int SpotHealCancellable([In,Out] byte[] pixels, byte[] coverage, int width, int height, float opacity, int mode, uint seed, HealingCancellation cancel, IntPtr context);
    [DllImport("Compositor.Native", EntryPoint="compositor_spot_heal", CallingConvention=CallingConvention.Cdecl)]
    internal static extern int SpotHeal([In,Out] byte[] pixels, byte[] coverage, int width, int height, float opacity, int mode, uint seed);
    [DllImport("Compositor.Native", EntryPoint="compositor_clone_coverage", CallingConvention=CallingConvention.Cdecl)]
    internal static extern int CloneCoverage([In,Out] float[] coverage, [Out] byte[] delta,
        int tileX, int tileY, int width, int height, double[] map, double ax, double ay, double bx, double by,
        double radius, double hardness, int canvasW, int canvasH);
    [DllImport("Compositor.Native",EntryPoint="compositor_wand_mask",CallingConvention=CallingConvention.Cdecl)]
    internal static extern long WandMask(byte[] pixels,int width,int height,int x,int y,int radius,int tolerance,int contiguous,[Out]byte[] mask);
    [DllImport("Compositor.Native",EntryPoint="compositor_wand_trace",CallingConvention=CallingConvention.Cdecl)]
    internal static extern int WandTrace(byte[] mask,int width,int height,out IntPtr points,out long pointCount,out IntPtr loops,out long loopCount);
    [DllImport("Compositor.Native",EntryPoint="compositor_free",CallingConvention=CallingConvention.Cdecl)]
    internal static extern void Free(IntPtr pointer);
    [DllImport("Compositor.Native", EntryPoint="compositor_content_fill", CallingConvention=CallingConvention.Cdecl)]
    internal static extern int ContentFill([In,Out] byte[] pixels,[In] byte[] mask,int width,int height);
    [DllImport("Compositor.Native", EntryPoint="compositor_lens", CallingConvention=CallingConvention.Cdecl)]
    internal static extern void Lens([In] byte[] source,[Out] byte[] destination,int width,int height,double strength);
    [DllImport("Compositor.Native", EntryPoint="compositor_add_noise", CallingConvention=CallingConvention.Cdecl)]
    internal static extern void AddNoise([In,Out] byte[] pixels,int width,int height,float amount,int gaussian,int monochromatic,uint seed);
    [DllImport("Compositor.Native", EntryPoint = "compositor_abi_version", CallingConvention = CallingConvention.Cdecl)]
    public static extern int AbiVersion();
    [DllImport("Compositor.Native", EntryPoint = "compositor_alpha_bounds", CallingConvention = CallingConvention.Cdecl)]
    public static extern void AlphaBounds(byte[] pixels, int width, int height, int stride, [Out] int[] bounds);
    [DllImport("Compositor.Native", EntryPoint = "compositor_clamp", CallingConvention = CallingConvention.Cdecl)]
    public static extern void Clamp([In, Out] byte[] pixels, int count);
    [DllImport("Compositor.Native", EntryPoint="compositor_color_balance", CallingConvention=CallingConvention.Cdecl)]
    internal static extern void ColorBalance([In,Out] byte[] pixels,int count,float[] shadows,float[] midtones,float[] highlights,int preserve);
    [DllImport("Compositor.Native", EntryPoint="compositor_hue_cube", CallingConvention=CallingConvention.Cdecl)]
    internal static extern void HueCube([In,Out] byte[] pixels,int count,float[] cube);
    [DllImport("Compositor.Native", EntryPoint="compositor_gradient_map", CallingConvention=CallingConvention.Cdecl)]
    internal static extern void GradientMap([In,Out] byte[] pixels,int count,byte[] table);
    [DllImport("Compositor.Native", EntryPoint="compositor_grain", CallingConvention=CallingConvention.Cdecl)]
    internal static extern void Grain([In,Out] byte[] pixels,int width,int height,double amount,double size,double roughness,uint seed,double originX,double originY,double units);
    [DllImport("Compositor.Native", EntryPoint = "compositor_black_white", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void BlackWhite([In, Out] byte[] pixels, int count, float[] weights, int tint, double hue, double saturation);
    [DllImport("Compositor.Native", EntryPoint = "compositor_levels", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void Levels([In, Out] byte[] pixels, int count, float[] tables);
    [DllImport("Compositor.Native", EntryPoint = "compositor_brush", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int Brush([In, Out] byte[] output, byte[] original, [In, Out] float[] coverage,
        int tileX, int tileY, int width, int height, double[] map, double ax, double ay, double bx, double by,
        double radius, double hardness, double opacity, int red, int green, int blue, int erase, int canvasW, int canvasH);
}
public sealed class BrushStroke
{
    private sealed record Work(byte[] Original, byte[] Output, float[] Coverage, float[]? Selection, byte[]? Delta);
    private readonly SelectionCoverage? selection;
    private readonly Dictionary<TileKey, Work> work = [];
    private readonly Layer layer;
    private readonly BrushSettings settings;
    private readonly int canvasWidth, canvasHeight;
    private readonly double[] mapping;
    private readonly Raster? cloneSample;
    private readonly PointD cloneOffset;
    private readonly bool replaceClone;
    private PointD? previous;
    public Raster Pixels { get; private set; }
    internal BrushStroke ReframeAligned(Layer grown, RasterFrame relative)
    {
        if (relative.Left % 256 != 0 || relative.Top % 256 != 0 || relative.Left > 0 || relative.Top > 0 ||
            relative.Left + relative.Width < Pixels.Width || relative.Top + relative.Height < Pixels.Height)
            throw new ArgumentException("Brush work transfer requires tile-aligned expansion.");
        var next = new BrushStroke(grown, settings, canvasWidth, canvasHeight, selection, cloneSample, cloneOffset, replaceClone)
        { Pixels = ReferenceEquals(Pixels, layer.Pixels) ? grown.Pixels : RasterReframe.Apply(Pixels, relative), previous = previous };
        foreach (var (key, tile) in work)
        {
            var shifted = new TileKey(key.X - relative.Left / 256, key.Y - relative.Top / 256);
            int oldWidth = Math.Min(256, Pixels.Width - key.X * 256), oldHeight = Math.Min(256, Pixels.Height - key.Y * 256);
            if (oldWidth == 256 && oldHeight == 256) { next.work.Add(shifted, tile); continue; }
            // A cropped edge tile can contain nonzero bytes outside its old logical
            // dimensions. New cells must start from the expanded base, never padding.
            byte[] original = grown.Pixels.Tiles.TryGetValue(shifted, out var source) ? source.Bytes.ToArray() : new byte[PixelTile.ByteCount];
            var output = (byte[])original.Clone();
            for (int y = 0; y < oldHeight; y++) tile.Output.AsSpan(y * PixelTile.Stride, oldWidth * 4).CopyTo(output.AsSpan(y * PixelTile.Stride));
            float[]? weights = null;
            if (selection is not null)
            {
                weights = new float[256 * 256];
                for (int y = 0; y < Math.Min(256, relative.Height - shifted.Y * 256); y++)
                for (int x = 0; x < Math.Min(256, relative.Width - shifted.X * 256); x++)
                    weights[y * 256 + x] = (float)selection.Sample(grown.Transform.ToDocument(new(shifted.X * 256 + x + .5, shifted.Y * 256 + y + .5), relative.Width, relative.Height));
            }
            next.work.Add(shifted, new(original, output, tile.Coverage, weights, tile.Delta));
        }
        return next;
    }
    public BrushStroke(Layer layer, BrushSettings settings, int canvasWidth, int canvasHeight, SelectionCoverage? selection = null, Raster? cloneSample = null, PointD cloneOffset = default, bool replaceClone = false)
    {
        if (!double.IsFinite(settings.Diameter) || settings.Diameter < 1 || settings.Diameter > (replaceClone ? 2004 : 2000) ||
            !double.IsFinite(settings.Hardness) || settings.Hardness is < 0 or > 1 ||
            !double.IsFinite(settings.Opacity) || settings.Opacity is < 0 or > 1)
            throw new ArgumentOutOfRangeException(nameof(settings));
        if (layer.IsAdjustment) throw new InvalidOperationException("Adjustment layers have no image pixels. Select Edit mask to paint their coverage.");
        if (layer.IsGroup) throw new InvalidOperationException("Groups cannot be painted directly.");
        if (cloneSample is not null && (settings.Erase || cloneSample.Width != canvasWidth || cloneSample.Height != canvasHeight ||
            !double.IsFinite(cloneOffset.X) || !double.IsFinite(cloneOffset.Y))) throw new ArgumentException("Invalid clone sample or offset.");
        if (replaceClone && cloneSample is null) throw new ArgumentException("Replacement requires a sample.");
        this.cloneSample = cloneSample; this.cloneOffset = cloneOffset;this.replaceClone = replaceClone;
        this.selection = selection;
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
        if (selection?.IsEmpty == true) return;
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
                float[]? cachedWeights = null;
                if (selection is not null)
                {
                    cachedWeights = new float[256 * 256];
                    for (int y = 0; y < Math.Min(256, Pixels.Height - ty * 256); y++)
                    for (int x = 0; x < Math.Min(256, Pixels.Width - tx * 256); x++)
                        cachedWeights[y * 256 + x] = (float)selection.Sample(layer.Transform.ToDocument(new(tx * 256 + x + .5, ty * 256 + y + .5), Pixels.Width, Pixels.Height));
                }
                tile = new(original, (byte[])original.Clone(), new float[256 * 256], cachedWeights, cloneSample is null ? null : new byte[256 * 256]); work.Add(key, tile);
            }
            if (tile.Selection is { } empty && empty.AsSpan().IndexOfAnyExcept(0f) < 0) continue;
            int updated = cloneSample is not null
                ? NativePixels.CloneCoverage(tile.Coverage, tile.Delta!, tx * 256, ty * 256,
                    Math.Min(256, Pixels.Width - tx * 256), Math.Min(256, Pixels.Height - ty * 256), mapping,
                    start.X, start.Y, point.X, point.Y, radius, settings.Hardness, canvasWidth, canvasHeight)
                : NativePixels.Brush(tile.Output, tile.Original, tile.Coverage, tx * 256, ty * 256,
                Math.Min(256, Pixels.Width - tx * 256), Math.Min(256, Pixels.Height - ty * 256), mapping,
                start.X, start.Y, point.X, point.Y, radius, settings.Hardness, settings.Opacity,
                settings.Red, settings.Green, settings.Blue, settings.Erase ? 1 : 0, canvasWidth, canvasHeight);
            if (updated == 0) continue;
            byte[] output = tile.Output;
            if (cloneSample is not null)
            {
                for (int y = 0; y < Math.Min(256, Pixels.Height - ty * 256); y++)
                for (int x = 0; x < Math.Min(256, Pixels.Width - tx * 256); x++)
                {
                    int index = y * 256 + x;
                    if (tile.Delta![index] == 0) continue;
                    double amount = tile.Coverage[index] * settings.Opacity * (tile.Selection?[index] ?? 1);
                    if (amount <= 0) continue;
                    double localX = tx * 256 + x + .5, localY = ty * 256 + y + .5;
                    var mapped = new PointD(mapping[0] * localX + mapping[2] * localY + mapping[4], mapping[1] * localX + mapping[3] * localY + mapping[5]);
                    var sampled = CloneStamp.Sample(cloneSample, new(mapped.X + cloneOffset.X, mapped.Y + cloneOffset.Y));
                    double inverse = replaceClone ? 1 - amount : 1 - sampled.Alpha * amount / 255;
                    for (int c = 0; c < 4; c++)
                    {
                        double channel = c switch { 0 => sampled.Red, 1 => sampled.Green, 2 => sampled.Blue, _ => sampled.Alpha };
                        output[index * 4 + c] = (byte)Math.Clamp(Math.Round(channel * amount + tile.Original[index * 4 + c] * inverse, MidpointRounding.AwayFromZero), 0, 255);
                    }
                }
                if (tiles.TryGetValue(key, out var current) && current.Bytes.SequenceEqual(output)) continue;
                if (!tiles.ContainsKey(key) && output.AsSpan().IndexOfAnyExcept((byte)0) < 0) continue;
            }
            else if (tile.Selection is { } weights)
            {
                // Always blend the accumulated raw stroke against its initial pixels. Do not
                // multiply feather coverage into the previous preview on every pointer event.
                output = new byte[PixelTile.ByteCount];
                for (int p = 0; p < weights.Length; p++)
                    for (int c = 0; c < 4; c++)
                        output[p * 4 + c] = (byte)Math.Clamp(Math.Round(tile.Original[p * 4 + c] * (1 - (double)weights[p]) + tile.Output[p * 4 + c] * (double)weights[p], MidpointRounding.AwayFromZero), 0, 255);
                if (tiles.TryGetValue(key, out var current) && current.Bytes.SequenceEqual(output)) continue;
                if (!tiles.ContainsKey(key) && output.AsSpan().IndexOfAnyExcept((byte)0) < 0) continue;
            }
            changed = true;
            if (output.AsSpan().IndexOfAnyExcept((byte)0) < 0) tiles.Remove(key);
            else tiles[key] = new(output);
        }
        if (changed) Pixels = new(Pixels.Width, Pixels.Height, tiles.ToImmutable());
    }
}
