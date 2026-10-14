using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Input;
using System.Windows.Threading;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;
public partial class MainWindow
{
    internal async Task CloneBenchmark(string report, int side)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var doc = Document.Create(side, side);var layer = doc.Layers[0] with { Pixels = ShapeRaster.Create(new(ShapeKind.Rectangle, .8, .2, .1), 64, 64), Transform = new(100, 100, 64, 64) };doc = doc.Replace(layer);
        session.Load(doc);ToolPicker.SelectedIndex = 13;BrushSize.Text = "800";BrushHardness.Text = "70";BrushOpacity.Text = "50";Canvas.Fit();
        Canvas.CloneSource.Set(new(131.5, 131.5));
        var target = new PointD(side / 2d + .5, side / 2d + .5);
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
        long previous = Stopwatch.GetTimestamp();double maximumGap = 0;int ticks = 0;
        timer.Tick += (_, _) => { long now = Stopwatch.GetTimestamp();maximumGap = Math.Max(maximumGap, Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);previous = now;ticks++; };
        long allocated = GC.GetTotalAllocatedBytes(true);var clock = Stopwatch.StartNew();timer.Start();
        double beginMs, completionMs;
        try
        {
            Canvas.BeginPointer(target, ModifierKeys.None);beginMs = clock.Elapsed.TotalMilliseconds;
            if (!Canvas.IsClonePreparing || !ReferenceEquals(session.Document, doc)) throw new InvalidOperationException("Large clone did not prepare privately.");
            for (int i = 1; i < 48; i++) Canvas.MovePointer(new(target.X + i * 8, target.Y));
            Canvas.EndPointer(true);if (!await Canvas.CloneCompletion) throw new InvalidOperationException("Large clone completion failed.");
            completionMs = clock.Elapsed.TotalMilliseconds;
        }
        finally { timer.Stop(); }
        if (session.UndoCount != 1 || session.InTransaction || ticks == 0) throw new InvalidOperationException("Clone worker lost Undo or Dispatcher progress.");
        var result = session.Document;var painted = result.Layers[0];var local = painted.Transform.ToPixels(target, painted.Pixels.Width, painted.Pixels.Height);
        int x = (int)Math.Floor(local.X), y = (int)Math.Floor(local.Y);
        var tile = painted.Pixels.Tiles[new(x / 256, y / 256)];int p = ((y % 256) * 256 + x % 256) * 4;
        if (tile.Bytes[p + 3] != 128) throw new InvalidOperationException("Clone result alpha differs from frozen source/opacity.");
        session.Undo();if (!ReferenceEquals(session.Document, doc)) throw new InvalidOperationException("Large clone Undo lost source.");
        Canvas.BeginPointer(target, ModifierKeys.None);await Task.Delay(10);
        if (!Canvas.IsClonePreparing) throw new InvalidOperationException("Clone preparation completed before active cancellation probe.");
        clock.Restart();
        var escape = new KeyEventArgs(Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(this), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
        WindowKeyDown(this, escape);if (!escape.Handled) throw new InvalidOperationException("Large preparation Esc was not handled.");
        if (await Canvas.CloneCompletion || !ReferenceEquals(session.Document, doc) || session.InTransaction) throw new InvalidOperationException("Cancelled large clone published an edit.");
        double cancelMs = clock.Elapsed.TotalMilliseconds;
        using var process = Process.GetCurrentProcess();process.Refresh();
        File.WriteAllText(report, JsonSerializer.Serialize(new { side, points = 48, brush = 800, beginMs, completionMs, ticks, maximumDispatcherGapMs = maximumGap, cancelMs,
            allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576d, peakMiB = process.PeakWorkingSet64 / 1048576d,
            note = "Actual WPF, sparse 64px source in large canvas, one run; includes queued input, rendering and Esc after 10ms while preparing. Cancellation phase within capture/copy/queued append is unproven. Timer gap is sampled Dispatcher latency, not physical input. Peak includes setup." }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var tab in workspace.Documents) tab.Session.MarkSaved();Close();
    }
}
