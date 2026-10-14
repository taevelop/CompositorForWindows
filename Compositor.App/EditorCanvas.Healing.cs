using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private sealed record HealingDrag(Layer Layer, HealingStroke Stroke);
    private HealingDrag? healingDrag;
    private Preparation? healingPreparation;
    private readonly SemaphoreSlim healingWorkerGate = new(1, 1);
    public Func<HealingMode> ReadHealing { get; set; } = () => HealingMode.ContentAware;
    internal Task<bool> HealingCompletion { get; private set; } = Task.FromResult(true);
    internal bool IsHealingPreparing => healingPreparation is not null;
    internal Func<Document, Guid, byte[], double, HealingMode, uint, CancellationToken, Document>? HealingOverride { get; set; }

    private void BeginHealing(Layer layer, BrushSettings settings)
    {
        uint seed = (uint)Random.Shared.NextInt64(0, 1L << 32);var mode = ReadHealing();
        healingDrag = new(layer, new HealingStroke(Session.Document, layer.Id, settings, mode, seed));
    }
    private void MoveHealing(PointD point)
    {
        var drag = healingDrag!;drag.Stroke.Append(point);Session.Preview(drag.Stroke.Preview);
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
                    return drag.Stroke.Complete(token, compute);
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
