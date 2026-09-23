using Compositor.Core;

namespace Compositor.Imaging;

/// <summary>Brush paints grayscale coverage; eraser paints black (hides), without changing layer pixels.</summary>
public sealed class MaskStroke
{
    private readonly BrushStroke stroke;
    private LayerMask current;
    public LayerMask Mask => current;
    public MaskStroke(Layer layer, BrushSettings settings, int width, int height)
    {
        current = layer.Mask ?? throw new InvalidOperationException("Add a mask before painting it.");
        if (!current.Enabled) throw new InvalidOperationException("Enable the mask before painting it.");
        // Match the Mac mask brush: its grayscale control supplies the red component.
        byte gray = settings.Erase ? (byte)0 : settings.Red;
        stroke = new(layer with { Pixels = current.EditingPixels(layer.Pixels.Width, layer.Pixels.Height), Mask = null },
            settings with { Red = gray, Green = gray, Blue = gray, Erase = false }, width, height);
    }
    public void Append(PointD point)
    {
        var before = stroke.Pixels; stroke.Append(point);
        if (!ReferenceEquals(before, stroke.Pixels)) current = current.WithPixels(stroke.Pixels);
    }
}
