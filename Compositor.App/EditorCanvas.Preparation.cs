using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private Document? requestedRender,preparedRender,failedRender;
    private CancellationTokenSource? renderCancellation;
    private bool renderWorkerActive,canvasDisposed;
    internal Task RenderPreparation { get; private set; }=Task.CompletedTask;
    internal bool EnsurePrepared(Document document)
    {
        if(canvasDisposed)return false;
        bool large=(long)document.Width*document.Height>=1_000_000&&document.Layers.Any(layer=>layer.IsAdjustment);
        if(!large){if(!ReferenceEquals(requestedRender,document))CancelRenderPreparation();return true;}
        if(ReferenceEquals(preparedRender,document))return true;
        if(ReferenceEquals(failedRender,document))return false;
        if(!ReferenceEquals(requestedRender,document))
        {requestedRender=document;failedRender=null;renderCancellation?.Cancel();}
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
                using var cancellation=new CancellationTokenSource();renderCancellation=cancellation;
                try
                {
                    using var prepared=await PreparedDocumentRender.CreateAsync(document,cancellation.Token);
                    if(!canvasDisposed&&ReferenceEquals(requestedRender,document)&&ReferenceEquals(Session.Document,document))
                    {renderer.InstallPrepared(prepared,document);preparedRender=document;requestedRender=null;}
                    else if(ReferenceEquals(requestedRender,document))requestedRender=null;
                }
                catch(OperationCanceledException){}
                catch(Exception error)
                {
                    if(!canvasDisposed&&ReferenceEquals(requestedRender,document))
                    {failedRender=document;requestedRender=null;ReportError?.Invoke(error.Message);}
                }
                finally{renderCancellation=null;}
            }
        }
        finally{renderWorkerActive=false;if(!canvasDisposed)InvalidateVisual();}
    }
    private void CancelRenderPreparation()
    {requestedRender=null;preparedRender=null;failedRender=null;renderCancellation?.Cancel();}
    private void DrawPreparation(SKCanvas canvas)
    {
        using var paint=new SKPaint{Color=new SKColor(190,200,215),IsAntialias=true};
        using var font=new SKFont{Size=16};
        canvas.DrawText(failedRender is null?"Preparing image…":"Image preparation failed",24,40,SKTextAlign.Left,font,paint);
    }
}
