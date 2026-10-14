using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Compositor.Core;
using Compositor.Imaging;

internal static class WarpBenchmark
{
    internal static void Run(string report, SmearMode mode, int side, int count, int diameter)
    {
        var doc = Document.Create(side, side);var bytes = new byte[checked(side * side * 4)];
        for (int y = 0; y < side; y++) for (int x = 0; x < side; x++)
        { int p = (y * side + x) * 4;bytes[p + (x < side / 2 ? 0 : 1)] = 100;bytes[p + 3] = 128; }
        var layer = doc.Layers[0] with { Pixels = Raster.FromRgba(side, side, bytes) };doc = doc.Replace(layer);
        string sourceHash = Convert.ToHexString(SHA256.HashData(bytes));
        long allocated = GC.GetTotalAllocatedBytes(true);var clock = Stopwatch.StartNew();
        var stroke = new WarpLayerStroke(doc, layer.Id, new(diameter, .7, .6, 0, 0, 0), mode);double prepareMs = clock.Elapsed.TotalMilliseconds;
        clock.Restart();double maximumInputMs = 0;
        for (int i = 0; i < count; i++)
        { long start = Stopwatch.GetTimestamp();stroke.Append(new(side / 2d - 4 + i * 8, side / 2d));maximumInputMs = Math.Max(maximumInputMs, Stopwatch.GetElapsedTime(start).TotalMilliseconds); }
        double inputMs = clock.Elapsed.TotalMilliseconds;clock.Restart();var result = stroke.Complete();double completeMs = clock.Elapsed.TotalMilliseconds;
        double allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576d;
        result.Validate();if (ReferenceEquals(result, doc)) throw new InvalidOperationException("Warp benchmark did not edit.");
        using var process = Process.GetCurrentProcess();process.Refresh();double peakMiB = process.PeakWorkingSet64 / 1048576d;
        if (Convert.ToHexString(SHA256.HashData(layer.Pixels.ToRgba())) != sourceHash) throw new InvalidOperationException("Warp changed immutable source.");
        string pixelHash = Convert.ToHexString(SHA256.HashData(result.Layers[0].Pixels.ToRgba()));
        File.WriteAllText(report, JsonSerializer.Serialize(new { mode = mode.ToString(), side, count, diameter, dabs = stroke.Points.Count, prepareMs, inputMs, maximumInputMs, completeMs, allocatedMiB, peakMiB, pixelHash,
            note = "One dense synthetic premultiplied red/green split, core only. Setup excluded from allocations but included in peak; hashes excluded from timed allocation and peak. No WPF latency, natural-image quality, physical input, or Mac parity claim." }, new JsonSerializerOptions { WriteIndented = true }));
    }
}
