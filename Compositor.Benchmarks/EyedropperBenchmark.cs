using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
internal static class EyedropperBenchmark
{
    public static void Run(string path)
    {
        var doc=Document.Create(4000,4000);var bytes=new byte[4000*4000*4];
        for(int p=0;p<bytes.Length;p+=4){bytes[p]=80;bytes[p+1]=120;bytes[p+2]=40;bytes[p+3]=160;}
        var source=doc.Layers[0] with{Pixels=Raster.FromRgba(4000,4000,bytes)};
        doc=doc.Replace(source);var rows=new List<object>();
        foreach(string name in new[]{"plain","shadow","color-balance"})
        {
            var current=name switch{
                "shadow"=>doc.Replace(source with{Effects=new(Shadow:new(120,45,24))}),
                "color-balance"=>doc with{Layers=doc.Layers.Add(Layer.ColorBalanceLayer(4000,4000) with{ColorBalance=new(MidCyanRed:20)})},
                _=>doc};
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
            using var sampler=new CompositeColorSampler();using var process=Process.GetCurrentProcess();
            process.Refresh();double baseline=process.WorkingSet64/1048576d;long allocated=GC.GetTotalAllocatedBytes(true);
            var clock=Stopwatch.StartNew();var first=sampler.Sample(current,new(1200,1500));double coldMs=clock.Elapsed.TotalMilliseconds;
            var times=new List<double>();
            for(int i=0;i<20;i++){clock.Restart();if(sampler.Sample(current,new(1200+i*50,1500+i*20)) is null)throw new InvalidOperationException("Missing sample");times.Add(clock.Elapsed.TotalMilliseconds);}
            var changed=current.Replace(current.Layers[0] with{Opacity=.7});
            clock.Restart();sampler.Sample(changed,new(1200,1500));double changedMs=clock.Elapsed.TotalMilliseconds;
            clock.Restart();var restored=sampler.Sample(current,new(1200,1500));double restoreMs=clock.Elapsed.TotalMilliseconds;
            if(restored!=first)throw new InvalidOperationException("Sampler cache did not restore the original color");
            process.Refresh();var sorted=times.Order().ToArray();
            rows.Add(new{name,coldMs,medianMs=sorted[10],p95Ms=sorted[18],maxMs=sorted[^1],changedMs,restoreMs,
                allocatedMiB=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d,baselineWorkingSetMiB=baseline,workingSetMiB=process.WorkingSet64/1048576d,
                processLifetimePeakMiB=process.PeakWorkingSet64/1048576d,times});
            Console.WriteLine($"{name}: cold {coldMs:F2} ms; median {sorted[10]:F2} ms; p95 {sorted[18]:F2} ms");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,os=RuntimeInformation.OSDescription,processors=Environment.ProcessorCount,
            description="4000-square premultiplied source. Persistent CPU sampler, 20 moving points, opacity edit and snapshot restore. Includes renderer caches; excludes WPF presentation/input. Managed allocation is cumulative; working set includes shared process state; peak is process lifetime, not scenario-local.",rows},new JsonSerializerOptions{WriteIndented=true}));
    }
}
