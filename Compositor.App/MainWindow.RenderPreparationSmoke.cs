using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task RenderPreparationSmokeTest()
    {
        var doc=Document.Create(1000,1000);var bytes=new byte[4_000_000];
        for(int i=0;i<bytes.Length;i+=4){bytes[i]=255;bytes[i+3]=255;}
        doc=doc.Replace(doc.Layers[0] with{Pixels=Raster.FromRgba(1000,1000,bytes)});
        doc=doc with{Layers=doc.Layers.Add(Layer.InvertLayer(1000,1000))};
        var editor=new EditorCanvas{Session=new EditorSession(doc)};string? error=null;editor.ReportError=value=>error=value;
        try
        {
            if(editor.EnsurePrepared(doc))throw new InvalidOperationException("Large uncached document rendered synchronously.");
            var pending=editor.RenderPreparation;editor.EnsurePrepared(doc);
            if(!ReferenceEquals(pending,editor.RenderPreparation))throw new InvalidOperationException("Duplicate preparation was started.");
            if(editor.SampleComposite(new(0,0)) is not null)throw new InvalidOperationException("Pending sample triggered synchronous rendering.");
            bool dispatched=false;await Dispatcher.InvokeAsync(()=>dispatched=true,DispatcherPriority.Input);
            if(!dispatched)throw new InvalidOperationException("Input dispatcher did not run.");
            var replacement=doc.Replace(doc.Layers[1] with{Visible=false});editor.Session.Load(replacement);editor.EnsurePrepared(replacement);
            await editor.RenderPreparation;
            if(error is not null||!editor.EnsurePrepared(replacement)||editor.SampleComposite(new(0,0))!=new SampledColor(255,0,0))
                throw new InvalidOperationException("Latest document was not prepared: "+error);
            editor.Session.Load(doc);editor.EnsurePrepared(doc);var closing=editor.RenderPreparation;editor.Dispose();await closing;
            if(editor.EnsurePrepared(doc))throw new InvalidOperationException("Closed canvas accepted prepared result.");
        }
        finally{editor.Dispose();}
    }
}
