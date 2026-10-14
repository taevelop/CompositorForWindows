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
    internal async Task HealingBenchmark(string report, int side)
    {
        await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
        var doc = Document.Create(side, side);
        var layer = doc.Layers[0] with { Pixels = ShapeRaster.Create(new(ShapeKind.Rectangle, .3, .5, .7), side, side) };
        var target = new PointD(side / 2d + .5, side / 2d + .5);
        var damage = new BrushStroke(layer, new(4, 1, 1, 0, 0, 0), side, side);damage.Append(target);
        layer = layer with { Pixels = damage.Pixels };doc = doc.Replace(layer);
        session.Load(doc);ToolPicker.SelectedIndex = 14;BrushSize.Text = "800";BrushHardness.Text = "70";BrushOpacity.Text = "100";Canvas.Fit();
        double initialPaintMs = 0;int initialPaints = 0;var initialClock = Stopwatch.StartNew();
        Canvas.PaintMeasured = (elapsed, _) => { initialPaints++;initialPaintMs += elapsed; };
        try { Canvas.InvalidateVisual();await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle); }
        finally { Canvas.PaintMeasured = null; }
        double initialDisplayMs = initialClock.Elapsed.TotalMilliseconds;
        if (initialPaints == 0) throw new InvalidOperationException("Initial document was not rendered before the input probe.");
        var timer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(10) };
        long previous = Stopwatch.GetTimestamp();double maximumGap = 0, maximumInput = 0;int ticks = 0;
        timer.Tick += (_, _) => { long now = Stopwatch.GetTimestamp();maximumGap = Math.Max(maximumGap, Stopwatch.GetElapsedTime(previous, now).TotalMilliseconds);previous = now;ticks++; };
        long allocated = GC.GetTotalAllocatedBytes(true);var clock = Stopwatch.StartNew();timer.Start();
        double paintTotalMs = 0, maximumPaintMs = 0;int paints = 0;
        Canvas.PaintMeasured = (elapsed, _) => { paints++;paintTotalMs += elapsed;maximumPaintMs = Math.Max(maximumPaintMs, elapsed); };
        double inputMs, workerMs;
        try
        {
            long start = Stopwatch.GetTimestamp();Canvas.BeginPointer(target, ModifierKeys.None);
            maximumInput = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            for (int i = 1; i < 48; i++)
            {
                start = Stopwatch.GetTimestamp();Canvas.MovePointer(new(target.X + i * 8, target.Y));
                maximumInput = Math.Max(maximumInput, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);
            }
            inputMs = clock.Elapsed.TotalMilliseconds;Canvas.EndPointer(true);clock.Restart();
            if (!await Canvas.HealingCompletion) throw new InvalidOperationException("Dense healing completion failed.");
            workerMs = clock.Elapsed.TotalMilliseconds;
        }
        finally { timer.Stop();Canvas.PaintMeasured = null; }
        if (ticks == 0 || session.UndoCount != 1 || session.InTransaction) throw new InvalidOperationException("Healing lost Dispatcher progress or single Undo.");
        var result = session.Document;var bytes = result.Layers[0].Pixels.ToRgba();var before = layer.Pixels.ToRgba();int pixel = ((int)target.Y * side + (int)target.X) * 4;
        if (bytes[pixel] <= before[pixel] || bytes[pixel + 3] != 255) throw new InvalidOperationException("Healing did not restore the defect.");
        session.Undo();if (!ReferenceEquals(session.Document, doc)) throw new InvalidOperationException("Dense healing Undo lost source.");
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Canvas.HealingOverride = (basis, id, coverage, opacity, mode, seed, token) =>
        {
            entered.TrySetResult();return SpotHealing.Compute(basis, id, coverage, opacity, mode, seed, token);
        };
        double cancelUiMs, cancelledWorkerMs;
        try
        {
            Canvas.BeginPointer(target, ModifierKeys.None);Canvas.EndPointer(true);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(20));
            if (!Canvas.IsHealingPreparing) throw new InvalidOperationException("Healing finished before cancellation probe.");
            clock.Restart();var escape = new KeyEventArgs(Keyboard.PrimaryDevice, System.Windows.PresentationSource.FromVisual(this), Environment.TickCount, Key.Escape) { RoutedEvent = Keyboard.PreviewKeyDownEvent };
            WindowKeyDown(this, escape);cancelUiMs = clock.Elapsed.TotalMilliseconds;
            if (!escape.Handled || session.InTransaction || !ReferenceEquals(session.Document, doc)) throw new InvalidOperationException("Healing Esc did not immediately restore source.");
            if (await Canvas.HealingCompletion) throw new InvalidOperationException("Cancelled healing published a result.");
            cancelledWorkerMs = clock.Elapsed.TotalMilliseconds;
            if (session.UndoCount != 0 || !ReferenceEquals(session.Document, doc)) throw new InvalidOperationException("Late healing changed cancelled source.");
        }
        finally { Canvas.HealingOverride = null; }
        using var process = Process.GetCurrentProcess();process.Refresh();
        File.WriteAllText(report, JsonSerializer.Serialize(new { side, points = 48, brush = 800, initialPaints, initialPaintMs, initialDisplayMs, inputMs, maximumInputMs = maximumInput, workerMs, ticks, maximumDispatcherGapMs = maximumGap, paints, paintTotalMs, maximumPaintMs, cancelUiMs, cancelledWorkerMs,
            allocatedMiB = (GC.GetTotalAllocatedBytes(true) - allocated) / 1048576d, peakMiB = process.PeakWorkingSet64 / 1048576d,
            note = "One actual WPF dense synthetic stroke after initial document rendering (reported separately); Background Dispatcher yield between inputs includes rendering. Paint timings exclude WPF bitmap presentation. Esc is issued after the real Compute delegate starts; exact native phase is unproven. UI restoration and worker drain are separate. Not physical-input latency, natural quality, or Mac parity. Allocation includes validation and cancellation probe; peak includes setup and initial rendering." }, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var tab in workspace.Documents) tab.Session.MarkSaved();Close();
    }
}
