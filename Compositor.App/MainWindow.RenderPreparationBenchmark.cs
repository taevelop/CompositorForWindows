using System.Diagnostics;
using System.Text.Json;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    internal async Task RenderPreparationBenchmark(string path)
    {
        var original=session.Document;var doc=Document.Create(4000,4000);var bytes=new byte[64_000_000];
        for(int p=0;p<bytes.Length;p+=4){bytes[p]=80;bytes[p+1]=120;bytes[p+2]=40;bytes[p+3]=160;}
        doc=doc.Replace(doc.Layers[0] with{Pixels=Raster.FromRgba(4000,4000,bytes)});
        doc=doc with{Layers=doc.Layers.Add(Layer.ColorBalanceLayer(4000,4000) with{ColorBalance=new(MidCyanRed:20)})};
        var gaps=new List<double>();var rows=new List<object>();var clock=Stopwatch.StartNew();double last=0;
        var timer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(16)};
        timer.Tick+=(_,_)=>{double now=clock.Elapsed.TotalMilliseconds;gaps.Add(now-last);last=now;};
        using var process=Process.GetCurrentProcess();long allocated=GC.GetTotalAllocatedBytes(true);
        try
        {
            timer.Start();
            for(int round=0;round<8;round++)
            {
                var pass=Stopwatch.StartNew();
                for(int edit=0;edit<5;edit++)
                {
                    doc=doc.Replace(doc.Layers[1] with{ColorBalance=new(MidCyanRed:10+round*5+edit)});
                    session.Load(doc);Canvas.EnsurePrepared(doc);Canvas.InvalidateVisual();await Task.Delay(20);
                }
                await Canvas.RenderPreparation;
                if(!Canvas.EnsurePrepared(doc)||Canvas.SampleComposite(new(1000,1000)) is null)throw new InvalidOperationException("Latest burst snapshot not ready.");
                await Dispatcher.InvokeAsync(()=>{},DispatcherPriority.ApplicationIdle);
                process.Refresh();rows.Add(new{round,burstCompleteMs=pass.Elapsed.TotalMilliseconds,workingSetMiB=process.WorkingSet64/1048576d,managedMiB=GC.GetTotalMemory(false)/1048576d});
            }
            timer.Stop();double allocatedMiB=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d;var sampled=Canvas.SampleComposite(new(1000,1000));
            var expected=await Task.Run(()=>{using var independent=new CompositeColorSampler();return independent.Sample(doc,new(1000,1000));});
            if(sampled!=expected)throw new InvalidOperationException("Final async frame sample differs from independent renderer.");
            var ordered=gaps.Order().ToArray();
            System.IO.File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,processors=Environment.ProcessorCount,
                description="Hidden WPF window, actual editor canvas, 4K color balance. Eight bursts of five edits 20ms apart. 16ms Input-priority Dispatcher timer gaps include scheduling/GC/painting, not physical input latency or visible presentation. No all-day leak claim. Final canonical color matched independent renderer.",
                ticks=gaps.Count,p95GapMs=ordered[(int)Math.Ceiling(ordered.Length*.95)-1],maxGapMs=ordered[^1],
                managedAllocatedMiB=allocatedMiB,rows},new JsonSerializerOptions{WriteIndented=true}));
        }
        finally{timer.Stop();session.Load(original);session.MarkSaved();Canvas.InvalidateVisual();}
    }
}
