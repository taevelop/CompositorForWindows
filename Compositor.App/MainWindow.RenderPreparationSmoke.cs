using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task RenderPreparationSmokeTest()
    {
        await GradientViewportPreparationSmokeTest();
        var doc=Document.Create(1000,1000);var bytes=new byte[4_000_000];
        for(int i=0;i<bytes.Length;i+=4){bytes[i]=255;bytes[i+3]=255;}
        bytes[4]=0;bytes[6]=255;
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
            editor.Session.Load(doc with{});
            editor.Tool=EditorTool.Eyedropper;int notifications=0;SampledColor? sampled=null;
            editor.ColorSampled=color=>{notifications++;sampled=color;};
            editor.BeginInteraction(System.Windows.Input.MouseButton.Left,new(30,30));
            editor.MoveInteraction(new(31,30));editor.FinishInteraction(System.Windows.Input.MouseButton.Left,new(31,30));
            await editor.SampleCompletion;
            if(sampled!=new SampledColor(255,255,0))throw new InvalidOperationException("Released deferred sample did not use final position.");
            notifications=0;var uncached=doc with{};editor.Session.Load(uncached);
            editor.BeginInteraction(System.Windows.Input.MouseButton.Left,new(30,30));editor.CancelInteraction();await editor.SampleCompletion;
            if(notifications!=0)throw new InvalidOperationException("Cancelled deferred sample changed foreground.");
            var replacement=doc.Replace(doc.Layers[1] with{Visible=false});editor.Session.Load(replacement);editor.EnsurePrepared(replacement);
            await editor.RenderPreparation;
            if(error is not null||!editor.EnsurePrepared(replacement)||editor.SampleComposite(new(0,0))!=new SampledColor(255,0,0))
                throw new InvalidOperationException("Latest document was not prepared: "+error);
            editor.Session.Load(doc);editor.EnsurePrepared(doc);var closing=editor.RenderPreparation;editor.Dispose();await closing;
            if(editor.EnsurePrepared(doc))throw new InvalidOperationException("Closed canvas accepted prepared result.");
        }
        finally{editor.Dispose();}
    }
    private async Task GradientViewportPreparationSmokeTest()
    {
        var doc=Document.Create(1000,1000);
        using var editor=new EditorCanvas{Session=new EditorSession(doc),Tool=EditorTool.Gradient};
        editor.Measure(new System.Windows.Size(400,300));editor.Arrange(new System.Windows.Rect(0,0,400,300));editor.RestoreView(.2,30,30);
        editor.ReadGradient=(start,end)=>new(start,end,255,0,0,0,0,255,Style:GradientStyle.ForegroundToBackground);
        editor.BeginInteraction(System.Windows.Input.MouseButton.Left,new(30.1,30.1));
        editor.FinishInteraction(System.Windows.Input.MouseButton.Left,new(229.9,30.1));await editor.GradientPending;
        if(!await editor.CommitGradientAsync())throw new InvalidOperationException("Preparation fixture commit failed.");
        var filled=editor.Session.Document;
        if(editor.EnsurePrepared(filled))throw new InvalidOperationException("Large gradient bypassed viewport preparation.");
        editor.RestoreView(.4,-50,-20);editor.EnsurePrepared(filled);await editor.RenderPreparation;
        if(!editor.EnsurePrepared(filled))throw new InvalidOperationException("Changed viewport was not prepared.");
        editor.RestoreView(.6,-100,-70);editor.EnsurePrepared(filled);
        var stale=editor.RenderPreparation;editor.Session.Load(doc);editor.EnsurePrepared(doc);await stale;
        if(!editor.EnsurePrepared(doc)||!ReferenceEquals(editor.Session.Document,doc))throw new InvalidOperationException("Stale viewport replaced a new document.");
    }
}
