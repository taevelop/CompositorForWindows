using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private sealed class ClonePreparation(EditorSession session, Document document, long generation, Guid layer)
    {
        public EditorSession Session { get; } = session;
        public Document Document { get; } = document;
        public long Generation { get; } = generation;
        public Guid Layer { get; } = layer;
        public EditorTool Tool { get; init; }
        public CancellationTokenSource Cancellation { get; } = new();
        public List<PointD> Points { get; } = [];
        public bool Ended { get; set; }
    }
    private ClonePreparation? clonePreparation;
    private readonly SemaphoreSlim cloneWorkerGate = new(1, 1);
    internal Task<bool> CloneCompletion { get; private set; } = Task.FromResult(true);
    internal bool IsClonePreparing => clonePreparation is not null;
    internal Func<Document, Guid, CloneSettings, CancellationToken, Raster>? CloneCaptureOverride { get; set; }
    internal Func<Document, Guid, double, CancellationToken, Raster>? BlurCaptureOverride { get; set; }
    private static bool OwnsCloneTransaction(ClonePreparation owner) => owner.Session.InTransaction && owner.Session.TransactionGeneration == owner.Generation && ReferenceEquals(owner.Session.Document, owner.Document);
    private bool OwnsClone(ClonePreparation owner) => !canvasDisposed && ReferenceEquals(clonePreparation, owner) && ReferenceEquals(Session, owner.Session)
        && Tool == owner.Tool && owner.Session.ActiveLayerId == owner.Layer && !owner.Session.EditMask && OwnsCloneTransaction(owner);

    private void BeginClonePreparation(Layer layer, BrushSettings brush, CloneSettings settings, PointD offset, PointD point)
    {
        Session.Begin();var owner = new ClonePreparation(Session, Session.Document, Session.TransactionGeneration, layer.Id) { Tool = Tool };
        owner.Points.Add(point);clonePreparation = owner;
        CloneCompletion = PrepareCloneAsync(owner, layer.Id, brush, settings, offset, CloneCaptureOverride, BlurCaptureOverride);InvalidateVisual();
    }
    private void CancelClonePreparation()
    {
        if (clonePreparation is not { } owner) return;
        bool owns = OwnsCloneTransaction(owner);clonePreparation = null;owner.Cancellation.Cancel();
        if (owns) owner.Session.Cancel();
        if (ReferenceEquals(Session, owner.Session)) originalLayer = null;
        if (!canvasDisposed) InvalidateVisual();
    }
    private async Task<bool> PrepareCloneAsync(ClonePreparation owner, Guid layer, BrushSettings brush, CloneSettings settings, PointD offset,
        Func<Document, Guid, CloneSettings, CancellationToken, Raster>? capture,
        Func<Document, Guid, double, CancellationToken, Raster>? blurCapture)
    {
        var token = owner.Cancellation.Token;bool installed = false;
        try
        {
            var prepared = await Task.Run(async () =>
            {
                await cloneWorkerGate.WaitAsync(token);
                try
                {
                    var sample = owner.Tool == EditorTool.Blur
                        ? (blurCapture is { } blur ? blur(owner.Document, layer, brush.Diameter, token) : BlurBrush.Capture(owner.Document, layer, brush.Diameter, token))
                        : capture is null ? CloneStamp.Capture(owner.Document, layer, settings, token) : capture(owner.Document, layer, settings, token);
                    token.ThrowIfCancellationRequested();return new CloneStroke(owner.Document, layer, brush, sample, offset);
                }
                finally { cloneWorkerGate.Release(); }
            });
            while (OwnsClone(owner) && !token.IsCancellationRequested)
            {
                var points = owner.Points.ToArray();owner.Points.Clear();
                if (points.Length == 0) break;
                await Task.Run(() => { foreach (var point in points) { token.ThrowIfCancellationRequested();prepared.Append(point); } }, token);
            }
            if (!OwnsClone(owner) || token.IsCancellationRequested) return false;
            clonePreparation = null;cloneStroke = prepared;installed = true;
            owner.Session.Preview(prepared.Document);
            if (owner.Ended) EndPointer(true);
            return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception error) { if (OwnsClone(owner)) ReportError?.Invoke(error.Message);return false; }
        finally
        {
            if (!installed && OwnsCloneTransaction(owner)) owner.Session.Cancel();
            if (!installed && ReferenceEquals(clonePreparation, owner) && ReferenceEquals(Session, owner.Session)) originalLayer = null;
            if (ReferenceEquals(clonePreparation, owner)) clonePreparation = null;
            owner.Cancellation.Dispose();if (!canvasDisposed) InvalidateVisual();
        }
    }
    private void DrawClonePreparation(SKCanvas canvas)
    {
        if (!IsClonePreparing) return;
        using var background = new SKPaint { Color = new SKColor(28, 30, 34, 230) };
        using var ink = new SKPaint { Color = new SKColor(220, 230, 245), IsAntialias = true };using var font = new SKFont { Size = 13 };
        canvas.DrawRoundRect(new SKRect(12, 12, 300, 44), 4, 4, background);
        canvas.DrawText(clonePreparation?.Tool == EditorTool.Blur ? "Preparing blur sample · Esc to cancel" : "Preparing clone sample · Esc to cancel", 22, 33, SKTextAlign.Left, font, ink);
    }
}
