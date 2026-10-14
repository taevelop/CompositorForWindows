using Compositor.Core;

namespace Compositor.Imaging;

/// <summary>Original pixels, full healing coverage and clipped wash share one expanding raster grid.</summary>
public sealed class HealingStroke
{
    private readonly Document original;
    private readonly Layer source;
    private readonly BrushSettings settings;
    private readonly HealingMode mode;
    private readonly uint seed;
    private readonly SelectionCoverage? selection;
    private readonly List<PointD> points = [];
    private RasterFrame frame;
    private Layer expanded;
    private BrushStroke coverage, wash;
    public Document Preview { get; private set; }
    public Document Base => original.Replace(expanded);
    public Guid LayerId => source.Id;

    public HealingStroke(Document document, Guid layerId, BrushSettings settings, HealingMode mode, uint seed)
    {
        document.Validate();original = Preview = document;source = document.Layers.First(l => l.Id == layerId);
        this.settings = settings;this.mode = mode;this.seed = seed;
        if (source.IsGroup || source.IsAdjustment || settings.Erase || !Enum.IsDefined(mode)) throw new InvalidOperationException("Select image pixels and healing settings.");
        selection = document.Selection is { } selected ? SelectionCoverage.Create(selected, document.Width, document.Height) : null;
        frame = new(0, 0, source.Pixels.Width, source.Pixels.Height);expanded = source;(coverage, wash) = Create(source);
    }
    private (BrushStroke, BrushStroke) Create(Layer layer) => (
        new(layer with { Pixels = new Raster(layer.Pixels.Width, layer.Pixels.Height), Mask = null }, settings with { Opacity = 1, Red = 255, Green = 255, Blue = 255 }, original.Width, original.Height),
        new(layer, settings with { Opacity = .45, Red = 31, Green = 31, Blue = 31 }, original.Width, original.Height, selection));

    public void Append(PointD point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentException("Invalid healing pointer.");
        if (selection?.IsEmpty == true) return;
        var previous = points.Count == 0 ? point : points[^1];points.Add(point);double radius = settings.Diameter / 2;
        double x0 = Math.Clamp(Math.Min(previous.X, point.X) - radius, 0, original.Width), x1 = Math.Clamp(Math.Max(previous.X, point.X) + radius, 0, original.Width);
        double y0 = Math.Clamp(Math.Min(previous.Y, point.Y) - radius, 0, original.Height), y1 = Math.Clamp(Math.Max(previous.Y, point.Y) + radius, 0, original.Height);
        if (x1 > x0 && y1 > y0)
        {
            PointD[] corners = [new(x0, y0), new(x1, y0), new(x0, y1), new(x1, y1)];
            static double Snap(double v) => Math.Abs(v - Math.Round(v)) < 1e-7 ? Math.Round(v) : v;
            var local = corners.Select(p => source.Transform.ToPixels(p, source.Pixels.Width, source.Pixels.Height)).ToArray();
            int left = (int)Math.Floor(Math.Min(frame.Left, Snap(local.Min(p => p.X)))), top = (int)Math.Floor(Math.Min(frame.Top, Snap(local.Min(p => p.Y))));
            int right = (int)Math.Ceiling(Math.Max(frame.Left + frame.Width, Snap(local.Max(p => p.X)))), bottom = (int)Math.Ceiling(Math.Max(frame.Top + frame.Height, Snap(local.Max(p => p.Y))));
            var next = new RasterFrame(left, top, checked(right - left), checked(bottom - top));
            if (next != frame)
            {
                CheckBudget(next);var grown = RasterReframe.Apply(source, next);
                var relative = new RasterFrame(next.Left - frame.Left, next.Top - frame.Top, next.Width, next.Height);
                if (relative.Left % 256 == 0 && relative.Top % 256 == 0)
                {
                    // Keep the exact kernel frame and transfer aligned work instead of replaying the stroke.
                    coverage = coverage.ReframeAligned(grown with { Pixels = new Raster(next.Width, next.Height), Mask = null }, relative);
                    wash = wash.ReframeAligned(grown, relative);
                    coverage.Append(point);wash.Append(point);
                }
                else { (coverage, wash) = Create(grown);foreach (var painted in points) { coverage.Append(painted);wash.Append(painted); } }
                expanded = grown;frame = next;
            }
            else { coverage.Append(point);wash.Append(point); }
        }
        else { coverage.Append(point);wash.Append(point); }
        Preview = original.Replace(expanded with { Pixels = wash.Pixels });Preview.Validate();
    }
    private void CheckBudget(RasterFrame next)
    {
        next.Validate();long image = (long)next.Width * next.Height;
        long mask = source.Mask is null ? 0 : source.Mask.Linked && source.Mask.Placement is null ? image : (long)source.Mask.Pixels.Width * source.Mask.Pixels.Height;
        foreach (var layer in original.Layers.Where(l => l.Id != source.Id))
        {
            if (!layer.IsAdjustment && (layer.Pixels.Tiles.Count != 0 || layer.Mask is not null)) image += (long)layer.Pixels.Width * layer.Pixels.Height;
            if (layer.Mask is { } m) mask += (long)m.Pixels.Width * m.Pixels.Height;
        }
        if (image > Limits.MaxPixels || mask > Limits.MaxPixels) throw new InvalidDataException("Healing expansion exceeds the image or mask budget.");
    }
    public byte[] Coverage(CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested();
        var raster = coverage.Pixels;var result = new byte[checked(raster.Width * raster.Height)];
        foreach (var (key, tile) in raster.Tiles)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int y = 0; y < Math.Min(256, raster.Height - key.Y * 256); y++) for (int x = 0; x < Math.Min(256, raster.Width - key.X * 256); x++)
                result[(key.Y * 256 + y) * raster.Width + key.X * 256 + x] = tile.Bytes[(y * 256 + x) * 4 + 3];
        }
        cancellation.ThrowIfCancellationRequested();return result;
    }
    public Document Complete(CancellationToken cancellation = default,
        Func<Document, Guid, byte[], double, HealingMode, uint, CancellationToken, Document>? compute = null)
    {
        cancellation.ThrowIfCancellationRequested();
        var basis = Base;var gray = Coverage(cancellation);
        var result = compute is null ? SpotHealing.Compute(basis, source.Id, gray, settings.Opacity, mode, seed, cancellation)
            : compute(basis, source.Id, gray, settings.Opacity, mode, seed, cancellation);
        var layer = result.Layers.First(l => l.Id == source.Id);
        if (ReferenceEquals(layer.Pixels, expanded.Pixels)) return original;
        int left = -frame.Left, top = -frame.Top, right = left + source.Pixels.Width, bottom = top + source.Pixels.Height;
        int originalLeft = left, originalTop = top, originalRight = right, originalBottom = bottom;
        foreach (var (key, tile) in layer.Pixels.Tiles)
        {
            cancellation.ThrowIfCancellationRequested();int x = key.X * 256, y = key.Y * 256, width = Math.Min(256, layer.Pixels.Width - x), height = Math.Min(256, layer.Pixels.Height - y);
            if (x >= originalLeft && y >= originalTop && x + width <= originalRight && y + height <= originalBottom) continue;
            var bounds = new int[4];NativePixels.AlphaBounds(tile.Bytes.ToArray(), width, height, PixelTile.Stride, bounds);
            if (bounds[2] <= bounds[0] || bounds[3] <= bounds[1]) continue;
            left = Math.Min(left, key.X * 256 + bounds[0]);top = Math.Min(top, key.Y * 256 + bounds[1]);right = Math.Max(right, key.X * 256 + bounds[2]);bottom = Math.Max(bottom, key.Y * 256 + bounds[3]);
        }
        result = result.Replace(RasterReframe.Apply(layer, new(left, top, right - left, bottom - top), cancellation));result.Validate();
        if (EditorSession.UndoBytesRequired(original, result) > EditorSession.MaxHistoryBytes) throw new InvalidOperationException("Healing stroke exceeds the 256 MiB Undo limit.");
        cancellation.ThrowIfCancellationRequested();return result;
    }
}
