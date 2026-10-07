using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class GradientBenchmark
{
    public static void Run(string path)
    {
        var document=Document.Create(4000,4000);
        var layer=document.Layers[0];
        var settings=new GradientFillSettings(new(.5,.5),new(3999.5,.5),255,0,0,0,0,255,
            Style:GradientStyle.ForegroundToBackground);
        var measurements=new List<object>();
        foreach(var shape in Enum.GetValues<GradientShape>())
        {
            for(int run=0;run<3;run++)
            {
                long before=GC.GetTotalAllocatedBytes(true);var timer=Stopwatch.StartNew();
                var result=GradientFill.Apply(document,layer.Id,settings with{Shape=shape});
                double elapsedMs=timer.Elapsed.TotalMilliseconds;
                var first=result.Layers[0].Pixels.Tiles[new(0,0)].Bytes;
                var last=result.Layers[0].Pixels.Tiles[new(15,0)].Bytes;
                int offset=(3999%256)*4;
                if(first[0]!=255||first[2]!=0||last[offset]!=0||last[offset+2]!=255||last[offset+3]!=255)
                    throw new InvalidOperationException("Gradient benchmark endpoint pixels differ.");
                measurements.Add(new{shape=shape.ToString(),run,elapsedMs,allocatedMiB=(GC.GetTotalAllocatedBytes(true)-before)/1048576d});
            }
        }
        using var cancellation=new CancellationTokenSource();
        var clock=Stopwatch.StartNew();double requestedMs=0;bool cancelled=false;
        using(var timer=new Timer(_=>{Volatile.Write(ref requestedMs,clock.Elapsed.TotalMilliseconds);cancellation.Cancel();},null,50,Timeout.Infinite))
        {
            try{GradientFill.Apply(document,layer.Id,settings,cancellation:cancellation.Token);}
            catch(OperationCanceledException){cancelled=true;}
        }
        double completedMs=clock.Elapsed.TotalMilliseconds;
        if(!cancelled)throw new InvalidOperationException("Gradient completed before scheduled cancellation was observed.");
        if(document.Layers[0].Pixels.Tiles.Count!=0)throw new InvalidOperationException("Gradient mutated its source.");
        using var process=Process.GetCurrentProcess();process.Refresh();
        File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,
            description="4000x4000 blank raster, opaque red-to-blue linear/radial, three independent fills per shape; endpoint pixels and source immutability checked. Core CPU time and cumulative managed allocation; excludes WPF display/input, latest-request scheduling and peak memory.",
            measurements,requestedMs,completedMs,cancellationObservedAfterMs=completedMs-requestedMs,
            workingSetMiB=process.WorkingSet64/1048576d},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(File.ReadAllText(path));
    }
}
