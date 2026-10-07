using System.Diagnostics;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    internal async Task GradientBenchmark(string path)
    {
        // The child canvas Loaded handler fits the view and cancels interaction.
        // Let the routed Loaded event finish before generating benchmark input.
        await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
        var original=session.Document;var tools=CaptureTabTools();var document=Document.Create(4000,4000);
        var gaps=new List<double>();var rows=new List<object>();var clock=Stopwatch.StartNew();double last=0;
        var changedPaints=new List<double>();var overlayPaints=new List<double>();
        Canvas.PaintMeasured=(elapsed,changed)=>(changed?changedPaints:overlayPaints).Add(elapsed);
        var timer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(16)};
        timer.Tick+=(_,_)=>{double now=clock.Elapsed.TotalMilliseconds;gaps.Add(now-last);last=now;};
        using var process=Process.GetCurrentProcess();long allocated=GC.GetTotalAllocatedBytes(true);
        try
        {
            ToolPicker.SelectedIndex=10;BrushColor.Text="#FF0000";SetBackgroundColor("#0000FF");
            GradientTransparent.IsChecked=false;GradientReverse.IsChecked=false;GradientOpacity.Value=100;
            timer.Start();
            for(int round=0;round<8;round++)
            {
                session.Load(document);Canvas.RestoreView(.15,30,30);
                GradientLinear.IsChecked=round%2==0;GradientRadial.IsChecked=round%2!=0;
                var pass=Stopwatch.StartNew();Canvas.BeginInteraction(MouseButton.Left,new(30.075,30.075));
                for(int edit=0;edit<5;edit++)
                {
                    Canvas.MoveInteraction(new(30+(3000+edit*200)*.15,30+(500-edit*100)*.15));
                    await Task.Delay(20);
                }
                var tail=Stopwatch.StartNew();Canvas.FinishInteraction(MouseButton.Left,new(629.925,30.075));
                await Canvas.GradientPending;double latestReadyMs=tail.Elapsed.TotalMilliseconds;
                if(!await Canvas.CommitGradientAsync()||session.UndoCount!=1||Canvas.HasGradient)
                    throw new InvalidOperationException("Gradient burst did not commit once.");
                Canvas.EnsurePrepared(session.Document);await Canvas.RenderPreparation;
                if(!Canvas.EnsurePrepared(session.Document))throw new InvalidOperationException("Latest viewport preparation was not installed.");
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                var first=session.Document.Layers[0].Pixels.Tiles[new(0,0)].Bytes;
                var end=session.Document.Layers[0].Pixels.Tiles[new(15,0)].Bytes;
                if(first[0]!=255||first[2]!=0||end[159*4]!=0||end[159*4+2]!=255)
                    throw new InvalidOperationException("Latest gradient request lost its endpoint pixels.");
                process.Refresh();rows.Add(new{round,shape=round%2==0?"Linear":"Radial",burstCompleteMs=pass.Elapsed.TotalMilliseconds,latestReadyMs,
                    workingSetMiB=process.WorkingSet64/1048576d,managedMiB=GC.GetTotalMemory(false)/1048576d});
            }
            var before=session.Document;
            Canvas.BeginInteraction(MouseButton.Left,new(30.075,30.075));Canvas.MoveInteraction(new(500,100));
            var pending=Canvas.GradientPending;var cancelClock=Stopwatch.StartNew();Canvas.CancelInteraction();Canvas.CancelGradient();await pending;
            double cancellationMs=cancelClock.Elapsed.TotalMilliseconds;
            if(!ReferenceEquals(before,session.Document)||session.InTransaction)throw new InvalidOperationException("Gradient cancellation changed the document.");
            timer.Stop();Canvas.PaintMeasured=null;double allocatedMiB=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d;
            var expected=await Task.Run(()=>GradientFill.Apply(document,document.Layers[0].Id,
                new GradientFillSettings(new(.5,.5),new(3999.5,.5),255,0,0,0,0,255,GradientShape.Radial,GradientStyle.ForegroundToBackground)));
            var actualPixels=before.Layers[0].Pixels;var expectedPixels=expected.Layers[0].Pixels;
            if(actualPixels.Tiles.Count!=expectedPixels.Tiles.Count||expectedPixels.Tiles.Any(pair=>
                !actualPixels.Tiles.TryGetValue(pair.Key,out var actual)||!actual.Bytes.SequenceEqual(pair.Value.Bytes)))
                throw new InvalidOperationException("Final gradient differs from independently calculated latest settings.");
            var ordered=gaps.Order().ToArray();
            static object PaintStats(List<double> values)
            {
                var sorted=values.Order().ToArray();
                return new{count=sorted.Length,totalMs=sorted.Sum(),p95Ms=sorted.Length==0?0:sorted[(int)Math.Ceiling(sorted.Length*.95)-1],maxMs=sorted.Length==0?0:sorted[^1]};
            }
            if(ordered.Length==0)throw new InvalidOperationException("No Dispatcher samples collected.");
            System.IO.File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,processors=Environment.ProcessorCount,
                description="Hidden actual WPF canvas, 4K blank image, eight alternating linear/radial bursts of five pointer moves 20ms apart, final endpoint request and single Undo commit. Endpoint pixels and cancellation restoration checked. Input-priority 16ms timer measures Dispatcher scheduling, not physical input or visible frame latency. Working set is sampled after bursts, not peak memory or leak proof.",
                ticks=gaps.Count,p95GapMs=ordered[(int)Math.Ceiling(ordered.Length*.95)-1],maxGapMs=ordered[^1],
                cancellationMs,finalPixelsMatched=true,changedDocumentPaint=PaintStats(changedPaints),sameDocumentPaint=PaintStats(overlayPaints),managedAllocatedMiB=allocatedMiB,rows},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{timer.Stop();Canvas.PaintMeasured=null;Canvas.CancelInteraction();Canvas.CancelGradient();session.Load(original);session.MarkSaved();RestoreTabTools(tools);Canvas.InvalidateVisual();}
    }
}
