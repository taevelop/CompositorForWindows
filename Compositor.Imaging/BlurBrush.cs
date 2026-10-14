using Compositor.Core;
using SkiaSharp;

namespace Compositor.Imaging;

/// <summary>Freezes the active raw layer in document space and softens it once per stroke.</summary>
public static class BlurBrush
{
    public static double Sigma(double diameter)
    {
        if (!double.IsFinite(diameter) || diameter is < 1 or > 2000) throw new ArgumentOutOfRangeException(nameof(diameter));
        return Math.Clamp(diameter / 10, 1.5, 30);
    }

    public static Raster Capture(Document document, Guid layerId, double diameter, CancellationToken cancellation = default)
    {
        double sigma = Sigma(diameter);document.Validate();cancellation.ThrowIfCancellationRequested();
        var layer = document.Layers.First(l => l.Id == layerId);
        if (layer.IsGroup || layer.IsAdjustment) throw new InvalidOperationException("Blur requires image pixels.");
        var sharp = CloneStamp.Capture(document, layerId, new(), cancellation);
        return Soften(sharp, sigma, SKShaderTileMode.Decal, cancellation);
    }
    public static Raster CaptureMask(Document document, Guid layerId, double diameter, CancellationToken cancellation = default)
    {
        double sigma = Sigma(diameter);document.Validate();cancellation.ThrowIfCancellationRequested();
        var layer = document.Layers.First(l => l.Id == layerId);
        var mask = layer.Mask ?? throw new InvalidOperationException("Add a mask before blurring it.");
        if (!mask.Enabled) throw new InvalidOperationException("Enable the mask before blurring it.");
        var placed = MaskPlacement.Resolve(mask, mask.Placement ?? layer.Transform, new(0, 0, document.Width, document.Height), document.Width, document.Height, cancellation);
        return Soften(placed.EditingPixels(document.Width, document.Height), sigma, SKShaderTileMode.Clamp, cancellation);
    }
    private static Raster Soften(Raster sharp, double sigma, SKShaderTileMode edge, CancellationToken cancellation)
    {
        using var input = new SKBitmap(CanvasRenderer.Info(sharp.Width, sharp.Height));
        RasterBuffer.CopyTo(sharp, input.GetPixelSpan(), cancellation);
        using var output = new SKBitmap(input.Info);
        using (var canvas = new SKCanvas(output))
        using (var filter = SKImageFilter.CreateBlur((float)sigma, (float)sigma, edge))
        using (var paint = new SKPaint { ImageFilter = filter, BlendMode = SKBlendMode.Src })
        { canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(input, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint); }
        cancellation.ThrowIfCancellationRequested();
        return Raster.FromRgba(sharp.Width, sharp.Height, output.GetPixelSpan(), cancellation);
    }
}

/// <summary>Paints a frozen softened sample in place, retaining the original Mac source-over contract.</summary>
public sealed class BlurStroke
{
    private readonly CloneStroke stroke;
    public Document Document => stroke.Document;
    public BlurStroke(Document document, Guid layerId, BrushSettings settings, CancellationToken cancellation = default)
    {
        if (settings.Erase) throw new ArgumentException("Blur cannot erase.");
        var sample = BlurBrush.Capture(document, layerId, settings.Diameter, cancellation);
        cancellation.ThrowIfCancellationRequested();
        stroke = new(document, layerId, settings, sample, new());
    }
    public void Append(PointD point) => stroke.Append(point);
    public Document Complete() => stroke.Complete();
}
