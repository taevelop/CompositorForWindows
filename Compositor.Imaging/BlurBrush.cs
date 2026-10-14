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
        using var input = new SKBitmap(CanvasRenderer.Info(document.Width, document.Height));
        RasterBuffer.CopyTo(sharp, input.GetPixelSpan(), cancellation);
        using var output = new SKBitmap(input.Info);
        using (var canvas = new SKCanvas(output))
        using (var filter = SKImageFilter.CreateBlur((float)sigma, (float)sigma, SKShaderTileMode.Decal))
        using (var paint = new SKPaint { ImageFilter = filter, BlendMode = SKBlendMode.Src })
        { canvas.Clear(SKColors.Transparent);canvas.DrawBitmap(input, 0, 0, new SKSamplingOptions(SKFilterMode.Nearest), paint); }
        cancellation.ThrowIfCancellationRequested();
        return Raster.FromRgba(document.Width, document.Height, output.GetPixelSpan(), cancellation);
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
