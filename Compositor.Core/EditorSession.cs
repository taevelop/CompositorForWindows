namespace Compositor.Core;

/// <summary>History shares immutable tiles. A drag or stroke is one transaction.</summary>
public sealed class EditorSession
{
    private readonly List<Document> undo = [];
    private readonly List<Document> redo = [];
    private Document? transaction;
    private Document? saved;
    public Document Document { get; private set; }
    public Guid? ActiveLayerId { get; set; }
    public Layer? ActiveLayer => Document.Layers.FirstOrDefault(l => l.Id == ActiveLayerId);
    public bool IsModified => !ReferenceEquals(saved, Document);
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public bool InTransaction => transaction is not null;
    public event Action? Changed;
    public EditorSession(Document document) { document.Validate(); Document = document; saved = document; ActiveLayerId = document.Layers.LastOrDefault()?.Id; }
    public void Load(Document document, Guid? active = null)
    {
        document.Validate(); Document = document; saved = document; transaction = null;
        undo.Clear(); redo.Clear(); ActiveLayerId = active ?? document.Layers.LastOrDefault()?.Id; Changed?.Invoke();
    }
    public void MarkSaved() { saved = Document; Changed?.Invoke(); }
    public void Begin() { if (transaction is not null) throw new InvalidOperationException("An edit is already active."); transaction = Document; }
    public void Preview(Document next)
    {
        if (transaction is null) throw new InvalidOperationException("Begin an edit first.");
        Document = next; Changed?.Invoke();
    }
    public void Commit()
    {
        if (transaction is null) return;
        var before = transaction;
        try { Document.Validate(); } catch { Cancel(); throw; }
        transaction = null;
        if (!ReferenceEquals(before, Document)) { undo.Add(before); redo.Clear(); TrimHistory(); }
        Changed?.Invoke();
    }
    public void Cancel() { if (transaction is not null) Document = transaction; transaction = null; ReconcileActive(); Changed?.Invoke(); }
    public void Apply(Func<Document, Document> edit)
    {
        if (InTransaction) throw new InvalidOperationException("Finish the active edit first.");
        Begin();
        try { Preview(edit(Document)); Commit(); } catch { Cancel(); throw; }
    }
    public void Undo()
    {
        if (InTransaction) { Cancel(); return; }
        if (undo.Count == 0) return;
        redo.Add(Document); Document = undo[^1]; undo.RemoveAt(undo.Count - 1); ReconcileActive(); Changed?.Invoke();
    }
    public void Redo()
    {
        if (InTransaction || redo.Count == 0) return;
        undo.Add(Document); Document = redo[^1]; redo.RemoveAt(redo.Count - 1); ReconcileActive(); Changed?.Invoke();
    }
    private void ReconcileActive() { if (ActiveLayer is null) ActiveLayerId = Document.Layers.LastOrDefault()?.Id; }
    private void TrimHistory()
    {
        while (undo.Count > 100) undo.RemoveAt(0);
        while (undo.Count > 0 && HistoryBytes() > 256L * 1024 * 1024) undo.RemoveAt(0);
    }
    private long HistoryBytes()
    {
        var current = Document.Layers.SelectMany(l => l.Pixels.Tiles.Values).ToHashSet();
        return undo.SelectMany(d => d.Layers).SelectMany(l => l.Pixels.Tiles.Values).Distinct()
            .Count(t => !current.Contains(t)) * (long)PixelTile.ByteCount;
    }
}
