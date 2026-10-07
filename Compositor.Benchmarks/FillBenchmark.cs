using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;
internal static class FillBenchmark
{
    public static void Run(string path)
    {
        var doc=Document.Create(4000,4000);var clock=Stopwatch.StartNew();long allocated=GC.GetTotalAllocatedBytes(true);
        var result=LayerFill.Apply(doc,doc.Layers[0].Id,23,84,192);double fillMs=clock.Elapsed.TotalMilliseconds;
        long fillAllocated=GC.GetTotalAllocatedBytes(true)-allocated;
        if(result.Layers[0].Pixels.Tiles[new(15,15)].Bytes[3]!=255)throw new InvalidOperationException("Fill did not reach last tile.");
        using var cancellation=new CancellationTokenSource();double requestedMs=0;

        clock.Restart();using var timer=new Timer(_=>{Volatile.Write(ref requestedMs,clock.Elapsed.TotalMilliseconds);cancellation.Cancel();},null,50,Timeout.Infinite);bool cancelled=false;
        try{LayerFill.Apply(doc,doc.Layers[0].Id,1,2,3,cancellation:cancellation.Token);}
        catch(OperationCanceledException){cancelled=true;}
        double completedMs=clock.Elapsed.TotalMilliseconds;
        if(!cancelled)throw new InvalidOperationException("Large fill completed without observing scheduled cancellation.");
        using var process=Process.GetCurrentProcess();process.Refresh();
        File.WriteAllText(path,JsonSerializer.Serialize(new{timeUtc=DateTimeOffset.UtcNow,description="4000-square empty image filled opaque. Scheduled cancellation after 50ms in a separate operation; measures core observation, not WPF button latency. Allocation is cumulative for completed fill, working set includes retained result and cancelled work.",
            fillMs,fillAllocatedMiB=fillAllocated/1048576d,requestedMs,completedMs,cancellationObservedAfterMs=completedMs-requestedMs,workingSetMiB=process.WorkingSet64/1048576d},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine(File.ReadAllText(path));
    }
}
