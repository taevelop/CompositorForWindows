using Compositor.Core;
using SkiaSharp;
namespace Compositor.Imaging;

public static class SubjectSelection
{
    public static Document Compute(Document document,Func<Raster,CancellationToken,LayerMask> predict,SelectionMode mode=SelectionMode.Replace,CancellationToken cancellation=default)
    {
        document.Validate();cancellation.ThrowIfCancellationRequested();
        using var renderer=new CanvasRenderer();using var bitmap=new SKBitmap(CanvasRenderer.Info(document.Width,document.Height));
        using(var canvas=new SKCanvas(bitmap)){canvas.Clear(SKColors.Transparent);renderer.DrawCancellable(canvas,document,cancellation);canvas.Flush();}
        var composite=Raster.FromRgba(document.Width,document.Height,bitmap.GetPixelSpan());
        cancellation.ThrowIfCancellationRequested();return MaskTracing.SelectSubject(document,predict(composite,cancellation),mode,cancellation);
    }
}
