using System.Diagnostics;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class BackgroundScaleBenchmark
{
    public static void Run(string model,string report,int side)
    {
        Limits.CheckDimensions(side,side);
        var source=ShapeRaster.Create(new(ShapeKind.Ellipse,.8,.2,.1),side,side);
        var original=Document.Create(side,side);var layer=original.Layers[0] with{Pixels=source};original=original.Replace(layer);
        int calls=0;double inferenceMs=0;
        LayerMask Predict(Raster pixels,CancellationToken token)
        {
            calls++;using var predictor=new U2NetPredictor(model);var clock=Stopwatch.StartNew();var result=predictor.Predict(pixels,token);inferenceMs=clock.Elapsed.TotalMilliseconds;return result;
        }
        var job=new BackgroundRemovalJob(original,layer.Id,Predict);long allocated=GC.GetTotalAllocatedBytes(true);var timer=Stopwatch.StartNew();
        var preview=job.Prepare(new(true),true);double previewTotalMs=timer.Elapsed.TotalMilliseconds;
        timer.Restart();var full=job.Prepare(new(true),false);double applyMs=timer.Elapsed.TotalMilliseconds;
        if(calls!=1||!ReferenceEquals(source,full.Layers[0].Pixels)||full.Layers[0].Mask!.Pixels.Width!=side||preview.Layers[0].Mask!.Pixels.Height!=side)throw new InvalidOperationException("Background scaling changed source ownership or inference count.");
        full.Validate();using var process=Process.GetCurrentProcess();process.Refresh();
        File.WriteAllText(report,JsonSerializer.Serialize(new{side,calls,inferenceMs,previewTotalMs,applyMs,allocatedMiB=(GC.GetTotalAllocatedBytes(true)-allocated)/1048576d,peakWorkingSetMiB=process.PeakWorkingSet64/1048576d,workingSetMiB=process.WorkingSet64/1048576d,note="Isolated core process, synthetic ellipse, Advanced 12/25/0. Preview includes model load/inference; apply reuses matte. OS process peak includes setup and retained preview/full documents. Not WPF latency, natural-image quality or sustained/complex-document stability proof."},new JsonSerializerOptions{WriteIndented=true}));
    }
}

