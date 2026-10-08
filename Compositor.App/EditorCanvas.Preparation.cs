using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private Document? requestedRender,preparedRender,failedRender;
    private RenderPreparationViewport? requestedViewport,preparedViewport,failedViewport;
    private RenderPreparationViewport? GradientViewport(Document document)=>ReferenceEquals(gradientRenderDocument,document)&&ActualWidth>0&&ActualHeight>0
        ?new((int)Math.Ceiling(ActualWidth),(int)Math.Ceiling(ActualHeight),(float)Zoom,(float)panX,(float)panY):null;
    private CancellationTokenSource? renderCancellation;
    private bool renderWorkerActive,canvasDisposed;
    internal Task RenderPreparation { get; private set; }=Task.CompletedTask;
    internal bool EnsurePrepared(Document document)
    {
        if(canvasDisposed)return false;
        if(!HasGradient&&!ReferenceEquals(gradientRenderDocument,document))gradientRenderDocument=null;
        var viewport=GradientViewport(document);
        bool large=(long)document.Width*document.Height>=1_000_000&&(viewport is not null||document.Layers.Any(layer=>layer.IsAdjustment));
        if(!large){if(!ReferenceEquals(requestedRender,document))CancelRenderPreparation();return true;}
        if(ReferenceEquals(preparedRender,document)&&preparedViewport==viewport)return true;
        if(ReferenceEquals(failedRender,document)&&failedViewport==viewport)return false;
        if(!ReferenceEquals(requestedRender,document)||requestedViewport!=viewport)
        {requestedRender=document;requestedViewport=viewport;failedRender=null;renderCancellation?.Cancel();}
        if(!renderWorkerActive)RenderPreparation=PrepareLatestRender();
        return false;
    }
    private async Task PrepareLatestRender()
    {
        renderWorkerActive=true;
        try
        {
            while(!canvasDisposed&&requestedRender is {} document)
            {
                var viewport=requestedViewport;
                using var cancellation=new CancellationTokenSource();renderCancellation=cancellation;
                try
                {
                    using var prepared=await (viewport is null?PreparedDocumentRender.CreateAsync(document,cancellation.Token):PreparedDocumentRender.CreateAsync(document,viewport,cancellation.Token));
                    if(!canvasDisposed&&ReferenceEquals(requestedRender,document)&&requestedViewport==viewport&&ReferenceEquals(DisplayDocument,document)&&GradientViewport(document)==viewport)
                    {renderer.InstallPrepared(prepared,document);preparedRender=document;preparedViewport=viewport;requestedRender=null;}
                    else if(ReferenceEquals(requestedRender,document)&&requestedViewport==viewport)requestedRender=null;
                }
                catch(OperationCanceledException){}
                catch(Exception error)
                {
                    if(!canvasDisposed&&ReferenceEquals(requestedRender,document)&&requestedViewport==viewport)
                    {failedRender=document;failedViewport=viewport;requestedRender=null;ReportError?.Invoke(error.Message);}
                }
                finally{renderCancellation=null;}
            }
        }
        finally{renderWorkerActive=false;if(!canvasDisposed)InvalidateVisual();}
    }
    private void CancelRenderPreparation()
    {requestedRender=null;preparedRender=null;failedRender=null;requestedViewport=null;preparedViewport=null;failedViewport=null;renderCancellation?.Cancel();}
    private void DrawPreparation(SKCanvas canvas)
    {
        using var paint=new SKPaint{Color=new SKColor(190,200,215),IsAntialias=true};
        using var font=new SKFont{Size=16};
        canvas.DrawText(failedRender is null?"Preparing image…":"Image preparation failed",24,40,SKTextAlign.Left,font,paint);
    }
}
