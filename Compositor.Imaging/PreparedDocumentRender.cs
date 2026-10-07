using Compositor.Core;
namespace Compositor.Imaging;

public sealed record RenderPreparationViewport(int Width,int Height,float Zoom,float OffsetX,float OffsetY)
{
    internal void Validate()
    {
        if(Width<1||Height<1||!float.IsFinite(Zoom)||Zoom<=0||!float.IsFinite(OffsetX)||!float.IsFinite(OffsetY))
            throw new ArgumentException("Invalid preparation viewport.");
    }
}

/// <summary>A worker-owned canonical render cache. Transfer once to the UI renderer,
/// or dispose it when the requesting document is no longer current.</summary>
public sealed class PreparedDocumentRender : IDisposable
{
    public Document Document { get; }
    private CanvasRenderer? renderer;
    private PreparedDocumentRender(Document document,CanvasRenderer renderer){Document=document;this.renderer=renderer;}
    public static Task<PreparedDocumentRender> CreateAsync(Document document,CancellationToken cancellationToken=default)
        =>CreateCoreAsync(document,null,cancellationToken);
    public static Task<PreparedDocumentRender> CreateAsync(Document document,RenderPreparationViewport viewport,CancellationToken cancellationToken=default)
    {
        ArgumentNullException.ThrowIfNull(viewport);viewport.Validate();
        return CreateCoreAsync(document,viewport,cancellationToken);
    }
    private static Task<PreparedDocumentRender> CreateCoreAsync(Document document,RenderPreparationViewport? viewport,CancellationToken cancellationToken)
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
                using(var canvas=new SkiaSharp.SKCanvas(bitmap))
                {
                    // The one-pixel target has the same document-space clip as the
                    // requested viewport. Tile images retain full source resolution.
                    if(viewport is not null)
                    {
                        canvas.Scale(1f/viewport.Width,1f/viewport.Height);
                        canvas.Translate(viewport.OffsetX,viewport.OffsetY);canvas.Scale(viewport.Zoom);
                    }
                    renderer.DrawCancellable(canvas,document,cancellationToken);
                }
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
