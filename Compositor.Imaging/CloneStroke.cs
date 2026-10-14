using Compositor.Core;

namespace Compositor.Imaging;

/// <summary>Expands the target grid without moving source pixels; samples stay frozen for the whole stroke.</summary>
public sealed class CloneStroke
{
    private readonly Document original;
    private readonly Layer source;
    private readonly BrushSettings settings;
    private readonly Raster sample;
    private readonly PointD offset;
    private readonly SelectionCoverage? selection;
    private readonly List<PointD> points = [];
    private RasterFrame frame;
    private Layer expanded;
    private BrushStroke stroke;
    public Document Document { get; private set; }

    public CloneStroke(Document document, Guid layerId, BrushSettings settings, Raster sample, PointD offset)
    {
        document.Validate(); original = Document = document; source = document.Layers.First(l => l.Id == layerId);
        this.settings = settings; this.sample = sample; this.offset = offset;
        selection = document.Selection is { } selected ? SelectionCoverage.Create(selected, document.Width, document.Height) : null;
        frame = new(0, 0, source.Pixels.Width, source.Pixels.Height); expanded = source;
        stroke = MakeStroke(source);
    }

    private BrushStroke MakeStroke(Layer layer) => new(layer, settings, original.Width, original.Height, selection, sample, offset);

    public void Append(PointD point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y)) throw new ArgumentException("Invalid clone pointer.");
        if (selection?.IsEmpty == true) return;
        var previous = points.Count == 0 ? point : points[^1]; double radius = settings.Diameter / 2;
        double x0 = Math.Clamp(Math.Min(previous.X, point.X) - radius, 0, original.Width);
        double x1 = Math.Clamp(Math.Max(previous.X, point.X) + radius, 0, original.Width);
        double y0 = Math.Clamp(Math.Min(previous.Y, point.Y) - radius, 0, original.Height);
        double y1 = Math.Clamp(Math.Max(previous.Y, point.Y) + radius, 0, original.Height);
        points.Add(point);
        if (x1 > x0 && y1 > y0)
        {
            PointD[] corners = [new(x0, y0), new(x1, y0), new(x0, y1), new(x1, y1)];
            var local = corners.Select(p => source.Transform.ToPixels(p, source.Pixels.Width, source.Pixels.Height)).ToArray();
            int left = checked((int)Math.Floor(Math.Min(frame.Left, local.Min(p => p.X))));
            int top = checked((int)Math.Floor(Math.Min(frame.Top, local.Min(p => p.Y))));
            int right = checked((int)Math.Ceiling(Math.Max(frame.Left + frame.Width, local.Max(p => p.X))));
            int bottom = checked((int)Math.Ceiling(Math.Max(frame.Top + frame.Height, local.Max(p => p.Y))));
            var next = new RasterFrame(left, top, checked(right - left), checked(bottom - top));
            if (next != frame)
            {
                CheckBudget(next);
                var grown = RasterReframe.Apply(source, next);
                var replay = MakeStroke(grown);
                foreach (var painted in points) replay.Append(painted);
                expanded = grown; stroke = replay; frame = next;
            }
            else stroke.Append(point);
        }
        else stroke.Append(point);
        if (ReferenceEquals(stroke.Pixels, expanded.Pixels)) { Document = original; return; }
        var result = original.Replace(expanded with { Pixels = stroke.Pixels }); result.Validate();
        if (EditorSession.UndoBytesRequired(original, result) > EditorSession.MaxHistoryBytes)
            throw new InvalidOperationException("Clone stroke exceeds the 256 MiB Undo limit.");
        Document = result;
    }

    private void CheckBudget(RasterFrame next)
    {
        next.Validate(); long image = (long)next.Width * next.Height;
        long mask = source.Mask is null ? 0 : source.Mask.Linked && source.Mask.Placement is null
            ? image : (long)source.Mask.Pixels.Width * source.Mask.Pixels.Height;
        foreach (var layer in original.Layers.Where(l => l.Id != source.Id))
        {
            if (!layer.IsAdjustment && (layer.Pixels.Tiles.Count != 0 || layer.Mask is not null)) image += (long)layer.Pixels.Width * layer.Pixels.Height;
            if (layer.Mask is { } m) mask += (long)m.Pixels.Width * m.Pixels.Height;
        }
        if (image > Limits.MaxPixels || mask > Limits.MaxPixels) throw new InvalidDataException("Clone expansion exceeds the project's image or mask budget.");
    }
}
