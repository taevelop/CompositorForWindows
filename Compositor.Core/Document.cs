using System.Collections.Immutable;

namespace Compositor.Core;

public enum BlendMode { Normal, Multiply, Screen, Overlay, Darken, Lighten, Difference }
public enum Sampling { Nearest, Smooth, High }
public readonly record struct PointD(double X, double Y);
public sealed record LayerTransform(double X, double Y, double Width, double Height,
    double Rotation = 0, bool FlipX = false, bool FlipY = false, Sampling Sampling = Sampling.High)
{
    public void Validate()
    {
        if (new[] { X, Y, Width, Height, Rotation }.Any(n => !double.IsFinite(n)) ||
            Math.Abs(X) > 1_000_000 || Math.Abs(Y) > 1_000_000 ||
            Width is < 1 or > 300_000 || Height is < 1 or > 300_000 || !Enum.IsDefined(Sampling))
            throw new InvalidDataException("Invalid layer transform.");
    }
    public PointD ToDocument(PointD p, int pixelWidth, int pixelHeight)
    {
        double x = (p.X / pixelWidth - .5) * Width * (FlipX ? -1 : 1);
        double y = (p.Y / pixelHeight - .5) * Height * (FlipY ? -1 : 1);
        double angle = Rotation * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        return new(X + Width / 2 + x * c - y * s, Y + Height / 2 + x * s + y * c);
    }
    public PointD ToPixels(PointD p, int pixelWidth, int pixelHeight)
    {
        double x = p.X - X - Width / 2, y = p.Y - Y - Height / 2;
        double angle = Rotation * Math.PI / 180, c = Math.Cos(angle), s = Math.Sin(angle);
        return new(((x * c + y * s) / Width * (FlipX ? -1 : 1) + .5) * pixelWidth,
            ((-x * s + y * c) / Height * (FlipY ? -1 : 1) + .5) * pixelHeight);
    }
}
public sealed record Layer(Guid Id, string Name, Raster Pixels, LayerTransform Transform,
    bool Visible = true, double Opacity = 1, BlendMode Blend = BlendMode.Normal, LayerMask? Mask = null, Guid? ParentId = null, bool IsGroup = false)
{
    public IEnumerable<PixelTile> RetainedTiles => Pixels.Tiles.Values.Concat(Mask?.Pixels.Tiles.Values ?? Enumerable.Empty<PixelTile>());
    public static Layer Group(string name, int width, int height, Guid? parent = null) =>
        new(Guid.NewGuid(), name, new(1, 1), new(0, 0, width, height), ParentId: parent, IsGroup: true);
    public static Layer Blank(string name, int width, int height) =>
        new(Guid.NewGuid(), name, new(width, height), new(0, 0, width, height));
}
public sealed record Document(Guid Id, int Width, int Height, double Resolution, ImmutableArray<Layer> Layers)
{
    public static Document Create(int width, int height)
    {
        Limits.CheckDimensions(width, height);
        return new(Guid.NewGuid(), width, height, 72, [Layer.Blank("Layer 1", width, height)]);
    }
    public void Validate()
    {
        Limits.CheckDimensions(Width, Height);
        if (Id == Guid.Empty || !double.IsFinite(Resolution) || Resolution is < 1 or > 9600 || Layers.Length > 10_000)
            throw new InvalidDataException("Invalid document metadata.");
        LayerHierarchy.Validate(Layers);
        var ids = new HashSet<Guid>();
        long pixels = 0, maskPixels = 0;
        foreach (var layer in Layers)
        {
            layer.Transform.Validate();
            if (layer.Id == Guid.Empty || !ids.Add(layer.Id) || string.IsNullOrWhiteSpace(layer.Name) ||
                System.Text.Encoding.UTF8.GetByteCount(layer.Name) > 16384 ||
                !double.IsFinite(layer.Opacity) || layer.Opacity is < 0 or > 1 || !Enum.IsDefined(layer.Blend))
                throw new InvalidDataException("Invalid layer metadata.");
            if (layer.Mask is { } mask)
            {
                var m = mask.Pixels;
                if (!(m.Width == 1 && m.Height == 1) && (m.Width != layer.Pixels.Width || m.Height != layer.Pixels.Height))
                    throw new NotSupportedException("This Windows build supports masks matching the source size or uniform 1x1 masks only.");
                maskPixels += (long)m.Width * m.Height;
            }
            if (layer.Pixels.Tiles.Count != 0 || layer.Mask is not null) pixels += (long)layer.Pixels.Width * layer.Pixels.Height;
        }
        if (maskPixels > Limits.MaxPixels) throw new InvalidDataException("The project exceeds 100 megapixels of masks.");
        if (pixels > Limits.MaxPixels) throw new InvalidDataException("The project exceeds 100 megapixels of source images.");
    }
    public Document Replace(Layer layer)
    {
        int index = -1;
        for (int i = 0; i < Layers.Length; i++) if (Layers[i].Id == layer.Id) index = i;
        if (index < 0) throw new InvalidOperationException("Layer no longer exists.");
        return this with { Layers = Layers.SetItem(index, layer) };
    }
}
