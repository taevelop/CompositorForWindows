using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private sealed class WarpOwner(EditorSession session, Document document, long generation, Guid layer, SmearMode mode)
    {
        public EditorSession Session { get; } = session;
        public Document Document { get; } = document;
        public long Generation { get; } = generation;
        public Guid Layer { get; } = layer;
        public SmearMode Mode { get; } = mode;
        public CancellationTokenSource Cancellation { get; } = new();
        public List<PointD> Points { get; } = [];
        public TaskCompletionSource Signal { get; set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool Ended { get; set; }
    }
    private WarpOwner? warpOwner;
    private Document? warpDisplay;
    private readonly SemaphoreSlim warpGate = new(1, 1);
    public Func<SmearMode> ReadSmear { get; set; } = () => SmearMode.Liquify;
    internal bool IsWarpPreparing => warpOwner is not null;
    internal Task<bool> WarpCompletion { get; private set; } = Task.FromResult(true);
    internal Func<Document, Guid, BrushSettings, SmearMode, CancellationToken, WarpLayerStroke>? WarpPrepareOverride { get; set; }
    private static bool OwnsWarpGeneration(WarpOwner owner) => owner.Session.InTransaction && owner.Session.TransactionGeneration == owner.Generation;
    private bool OwnsWarp(WarpOwner owner) => !canvasDisposed && ReferenceEquals(warpOwner, owner) && ReferenceEquals(Session, owner.Session)
        && OwnsWarpGeneration(owner) && ReferenceEquals(owner.Session.Document, owner.Document) && Tool == EditorTool.Blur && ReadSmear() == owner.Mode && !Session.EditMask && Session.ActiveLayerId == owner.Layer;
    private void BeginWarp(PointD point, BrushSettings settings)
    {
        if (Session.EditMask) throw new InvalidOperationException("Smudge and Liquify work on image pixels, not masks.");
        Session.Begin();var owner = new WarpOwner(Session, Session.Document, Session.TransactionGeneration, Session.ActiveLayerId!.Value, ReadSmear());
        owner.Points.Add(point);warpOwner = owner;WarpCompletion = RunWarp(owner, settings, WarpPrepareOverride);InvalidateVisual();
    }
    private void CancelWarp()
    {
        if (warpOwner is not { } owner) return;
        warpOwner = null;warpDisplay = null;owner.Cancellation.Cancel();
        if (OwnsWarpGeneration(owner)) owner.Session.Cancel();
        if (ReferenceEquals(Session, owner.Session)) originalLayer = null;
        if (!canvasDisposed) InvalidateVisual();
    }
    private async Task<bool> RunWarp(WarpOwner owner, BrushSettings settings,
        Func<Document, Guid, BrushSettings, SmearMode, CancellationToken, WarpLayerStroke>? prepare)
    {
        var token = owner.Cancellation.Token;bool gateHeld = false;
        try
        {
            await warpGate.WaitAsync(token);gateHeld = true;
            var stroke = await Task.Run(() => prepare is null ? new WarpLayerStroke(owner.Document, owner.Layer, settings, owner.Mode, token) : prepare(owner.Document, owner.Layer, settings, owner.Mode, token), token);
            while (OwnsWarp(owner) && !token.IsCancellationRequested)
            {
                var points = owner.Points.ToArray();owner.Points.Clear();
                if (points.Length > 0)
                {
                    await Task.Run(() => { foreach (var point in points) stroke.Append(point, token); }, token);
                    if (!OwnsWarp(owner) || token.IsCancellationRequested) return false;
                    var source = owner.Document.Layers.First(l => l.Id == owner.Layer);
                    var mask = source.Mask is { } m ? m with { Placement = m.Placement ?? source.Transform } : null;
                    warpDisplay = owner.Document.Replace(source with { Pixels = stroke.WorkingPixels, Transform = new(0, 0, owner.Document.Width, owner.Document.Height), Mask = mask });
                    InvalidateVisual();
                    continue;
                }
                if (owner.Ended) break;
                await owner.Signal.Task.WaitAsync(token);owner.Signal = new(TaskCreationOptions.RunContinuationsAsynchronously);
            }
            if (!OwnsWarp(owner) || token.IsCancellationRequested) return false;
            var result = await Task.Run(() => stroke.Complete(token), token);
            if (!OwnsWarp(owner) || token.IsCancellationRequested) return false;
            warpDisplay = null;owner.Session.Preview(result);owner.Session.Commit();return true;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return false; }
        catch (Exception error) { if (OwnsWarp(owner)) ReportError?.Invoke(error.Message);return false; }
        finally
        {
            if (gateHeld) warpGate.Release();
            if (OwnsWarpGeneration(owner)) owner.Session.Cancel();
            if (ReferenceEquals(warpOwner, owner)) { warpOwner = null;warpDisplay = null;if (ReferenceEquals(Session, owner.Session)) originalLayer = null; }
            owner.Cancellation.Dispose();if (!canvasDisposed) InvalidateVisual();
        }
    }
    private void DrawWarpStatus(SKCanvas canvas)
    {
        if (warpOwner is not { } owner) return;
        using var background = new SKPaint { Color = new SKColor(28, 30, 34, 230) };
        using var ink = new SKPaint { Color = new SKColor(220, 230, 245), IsAntialias = true };using var font = new SKFont { Size = 13 };
        canvas.DrawRoundRect(new SKRect(12, 12, 300, 44), 4, 4, background);
        canvas.DrawText(warpDisplay is null ? "Preparing Smear · Esc to cancel" : owner.Mode + " · Esc to cancel", 22, 33, SKTextAlign.Left, font, ink);
    }
}
