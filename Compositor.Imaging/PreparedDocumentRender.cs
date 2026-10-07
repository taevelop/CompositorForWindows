using Compositor.Core;
namespace Compositor.Imaging;

/// <summary>A worker-owned canonical render cache. Transfer once to the UI renderer,
/// or dispose it when the requesting document is no longer current.</summary>
public sealed class PreparedDocumentRender : IDisposable
{
    public Document Document { get; }
    private CanvasRenderer? renderer;
    private PreparedDocumentRender(Document document,CanvasRenderer renderer){Document=document;this.renderer=renderer;}
    public static Task<PreparedDocumentRender> CreateAsync(Document document,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Task.Run(()=>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var renderer=new CanvasRenderer();
            try
            {
                // A canonical adjustment composite is prepared by the same path as
                // screen/export, without retaining an additional full-size output.
                using(var bitmap=new SkiaSharp.SKBitmap(CanvasRenderer.Info(1,1)))
                using(var canvas=new SkiaSharp.SKCanvas(bitmap))renderer.DrawCancellable(canvas,document,cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                return new PreparedDocumentRender(document,renderer);
            }
            catch{renderer.Dispose();throw;}
        },cancellationToken);
    }
    internal CanvasRenderer Take(Document expected)
    {
        if(!ReferenceEquals(Document,expected))throw new InvalidOperationException("Prepared render belongs to a different document snapshot.");
        return Interlocked.Exchange(ref renderer,null)??throw new ObjectDisposedException(nameof(PreparedDocumentRender));
    }
    public void Dispose()=>Interlocked.Exchange(ref renderer,null)?.Dispose();
}
