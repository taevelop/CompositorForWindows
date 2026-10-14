using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private sealed record HealingDrag(Document Original, Layer Layer, BrushSettings Settings, HealingMode Mode, uint Seed, BrushStroke Coverage, BrushStroke Wash);
    private HealingDrag? healingDrag;
    private Preparation? healingPreparation;
    private readonly SemaphoreSlim healingWorkerGate = new(1, 1);
    public Func<HealingMode> ReadHealing { get; set; } = () => HealingMode.ContentAware;
    internal Task<bool> HealingCompletion { get; private set; } = Task.FromResult(true);
    internal bool IsHealingPreparing => healingPreparation is not null;
    internal Func<Document, Guid, byte[], double, HealingMode, uint, CancellationToken, Document>? HealingOverride { get; set; }

    private void BeginHealing(Layer layer, BrushSettings settings, SelectionCoverage? selection)
    {
        var coverage = new BrushStroke(layer with { Pixels = new Raster(layer.Pixels.Width, layer.Pixels.Height) }, settings with { Opacity = 1, Red = 255, Green = 255, Blue = 255 }, Session.Document.Width, Session.Document.Height);
        var wash = new BrushStroke(layer, settings with { Opacity = .45, Red = 31, Green = 31, Blue = 31 }, Session.Document.Width, Session.Document.Height, selection);
        healingDrag = new(Session.Document, layer, settings, ReadHealing(), (uint)Random.Shared.NextInt64(0, 1L << 32), coverage, wash);
    }
    private void MoveHealing(PointD point)
    {
        var drag = healingDrag!;drag.Coverage.Append(point);drag.Wash.Append(point);
        Session.Preview(drag.Original.Replace(drag.Layer with { Pixels = drag.Wash.Pixels }));
    }
    private void EndHealing(bool commit)
    {
        var drag = healingDrag!;healingDrag = null;originalLayer = null;gestureButton = null;
        if (!commit) { Session.Cancel(); return; }
        var owner = new Preparation(Session, Session.Document, Session.TransactionGeneration, new());healingPreparation = owner;
        HealingCompletion = CompleteHealingAsync(owner, drag, HealingOverride);InvalidateVisual();
    }
    private void CancelHealing()
    {
        healingDrag = null;
        if (healingPreparation is not { } owner) return;
        healingPreparation = null;owner.Cancellation.Cancel();if (Owns(owner)) owner.Session.Cancel();
    }
    private async Task<bool> CompleteHealingAsync(Preparation owner, HealingDrag drag,
        Func<Document, Guid, byte[], double, HealingMode, uint, CancellationToken, Document>? compute)
    {
        var token = owner.Cancellation.Token;
        try
        {
            var result = await Task.Run(async () =>
            {
                await healingWorkerGate.WaitAsync(token);
                try
                {
                    var raster = drag.Coverage.Pixels;var coverage = new byte[checked(raster.Width * raster.Height)];
                    foreach (var (key, tile) in raster.Tiles)
                    {
                        token.ThrowIfCancellationRequested();
                        for (int y = 0; y < Math.Min(256, raster.Height - key.Y * 256); y++)
                        for (int x = 0; x < Math.Min(256, raster.Width - key.X * 256); x++)
                            coverage[(key.Y * 256 + y) * raster.Width + key.X * 256 + x] = tile.Bytes[(y * 256 + x) * 4 + 3];
                    }
                    token.ThrowIfCancellationRequested();
                    return compute is null ? SpotHealing.Compute(drag.Original, drag.Layer.Id, coverage, drag.Settings.Opacity, drag.Mode, drag.Seed, token)
                        : compute(drag.Original, drag.Layer.Id, coverage, drag.Settings.Opacity, drag.Mode, drag.Seed, token);
                }
                finally { healingWorkerGate.Release(); }
            });
            if (canvasDisposed || !ReferenceEquals(healingPreparation, owner) || !ReferenceEquals(Session, owner.Session) || !Owns(owner) || token.IsCancellationRequested
                || Tool != EditorTool.SpotHealing || owner.Session.ActiveLayerId != drag.Layer.Id || owner.Session.EditMask) return false;
            owner.Session.Preview(result);owner.Session.Commit();return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception error) { if (!canvasDisposed && ReferenceEquals(healingPreparation, owner)) ReportError?.Invoke(error.Message);return false; }
        finally
        {
            if (Owns(owner)) owner.Session.Cancel();
            if (ReferenceEquals(healingPreparation, owner)) healingPreparation = null;
            owner.Cancellation.Dispose();if (!canvasDisposed) InvalidateVisual();
        }
    }
    private void DrawHealingStatus(SKCanvas canvas)
    {
        if (!IsHealingPreparing) return;
        using var background = new SKPaint { Color = new SKColor(28, 30, 34, 230) };
        using var ink = new SKPaint { Color = new SKColor(220, 230, 245), IsAntialias = true };using var font = new SKFont { Size = 13 };
        canvas.DrawRoundRect(new SKRect(12, 12, 280, 44), 4, 4, background);canvas.DrawText("Healing pixels · Esc to cancel", 22, 33, SKTextAlign.Left, font, ink);
    }
}
