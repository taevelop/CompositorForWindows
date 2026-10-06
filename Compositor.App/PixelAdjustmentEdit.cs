using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;

/// <summary>Reuses adjustment editors in a private source-sized document and previews only source pixels in the real document.</summary>
internal sealed class PixelAdjustmentEdit : IDisposable
{
    private readonly EditorSession actual;
    private readonly Document original;
    private readonly Layer source;
    private Layer? cachedAdjustment;
    private Raster? cachedPixels;
    private bool finished;
    public EditorSession PreviewSession { get; }
    public PixelAdjustmentEdit(EditorSession session)
    {
        if (session.InTransaction) throw new InvalidOperationException("Finish the active edit first.");
        source = session.ActiveLayer is { IsGroup: false, IsAdjustment: false } selected && !session.EditMask
            ? selected : throw new InvalidOperationException("Select image pixels, not a group, adjustment or mask.");
        if (!LayerHierarchy.Entries(session.Document).First(e => e.Layer.Id == source.Id).Visible)
            throw new InvalidOperationException("Show the layer and its parent groups before adjusting its pixels.");
        actual = session; original = session.Document;
        var document = Document.Create(source.Pixels.Width, source.Pixels.Height);
        document = document.Replace(document.Layers[0] with { Pixels = source.Pixels, Name = source.Name });
        PreviewSession = new(document);
        actual.Begin(); PreviewSession.Changed += PreviewChanged;
    }
    private void PreviewChanged()
    {
        var adjustment = PreviewSession.Document.Layers.LastOrDefault(l => l.IsAdjustment);
        if (adjustment is null) { actual.Preview(original); return; }
        if (cachedAdjustment != adjustment)
        {
            cachedPixels = PixelAdjustments.Apply(source.Pixels, adjustment);
            cachedAdjustment = adjustment;
        }
        var next = ReferenceEquals(cachedPixels, source.Pixels) ? original : original.Replace(source with { Pixels = cachedPixels! });
        if (EditorSession.UndoBytesRequired(original, next) > EditorSession.MaxHistoryBytes)
            throw new InvalidOperationException("This change exceeds the 256 MiB Undo limit. Cancel and use a smaller source image.");
        actual.Preview(next);
    }
    public void Complete()
    {
        if (finished) return;
        if (PreviewSession.UndoCount > 0) actual.Commit(); else actual.Cancel();
        finished = true;
    }
    public void Dispose()
    {
        PreviewSession.Changed -= PreviewChanged;
        if (!finished) { actual.Cancel(); finished = true; }
    }
}
