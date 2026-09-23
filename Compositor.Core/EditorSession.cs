namespace Compositor.Core;

/// <summary>History shares immutable tiles; saved revisions do not retain an extra document.</summary>
public sealed class EditorSession
{
    private sealed record State(Document Document, long Revision, Guid? ActiveLayer, bool EditMask);
    private readonly List<State> undo = [];
    private readonly List<State> redo = [];
    private State? transaction;
    private long revision, nextRevision, savedRevision;
    public Document Document { get; private set; }
    private Guid? activeLayerId;
    public Guid? ActiveLayerId { get => activeLayerId; set { if (activeLayerId != value) EditMask = false; activeLayerId = value; } }
    public bool EditMask { get; set; }
    public Layer? ActiveLayer => Document.Layers.FirstOrDefault(l => l.Id == ActiveLayerId);
    public bool IsModified => revision != savedRevision || (transaction is not null && !Equivalent(transaction.Document, Document));
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;
    public int UndoCount => undo.Count;
    public int RedoCount => redo.Count;
    public bool InTransaction => transaction is not null;
    public long HistoryRetainedBytes
    {
        get
        {
            var current = Document.Layers.SelectMany(l => l.RetainedTiles).ToHashSet();
            return undo.Concat(redo).SelectMany(s => s.Document.Layers).SelectMany(l => l.RetainedTiles)
                .Distinct().Count(t => !current.Contains(t)) * (long)PixelTile.ByteCount;
        }
    }
    public event Action? Changed;
    public EditorSession(Document document) { document.Validate(); Document = document; ActiveLayerId = document.Layers.LastOrDefault()?.Id; }

    public void Load(Document document, Guid? active = null, bool recovered = false)
    {
        document.Validate();
        if (active is not null && !document.Layers.Any(l => l.Id == active)) throw new InvalidDataException("Invalid active layer.");
        Document = document; revision = ++nextRevision; savedRevision = recovered ? -1 : revision;
        transaction = null; undo.Clear(); redo.Clear(); EditMask = false;
        ActiveLayerId = active ?? document.Layers.LastOrDefault()?.Id; Changed?.Invoke();
    }
    public void MarkSaved()
    {
        if (InTransaction) throw new InvalidOperationException("Finish the active edit before saving.");
        savedRevision = revision; Changed?.Invoke();
    }
    private State Capture() => new(Document, revision, ActiveLayerId, EditMask);
    private void Restore(State state)
    {
        Document = state.Document; revision = state.Revision; ActiveLayerId = state.ActiveLayer; EditMask = state.EditMask; ReconcileActive();
    }
    public void Begin()
    {
        if (transaction is not null) throw new InvalidOperationException("An edit is already active.");
        transaction = Capture();
    }
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
        if (Equivalent(before.Document, Document))
        {
            Document = before.Document; revision = before.Revision;
        }
        else
        {
            undo.Add(before); redo.Clear(); revision = ++nextRevision;
            ReconcileActive(); TrimHistory();
        }
        Changed?.Invoke();
    }
    public void Cancel()
    {
        if (transaction is { } before) Restore(before);
        transaction = null; ReconcileActive(); Changed?.Invoke();
    }
    public void Apply(Func<Document, Document> edit)
    {
        if (InTransaction) throw new InvalidOperationException("Finish the active edit first.");
        Begin();
        try { Preview(edit(Document)); Commit(); } catch { Cancel(); throw; }
    }
    public void Undo()
    {
        if (InTransaction) { Cancel(); return; }
        if (!CanUndo) return;
        redo.Add(Capture()); var state = undo[^1]; undo.RemoveAt(undo.Count - 1);
        Restore(state); TrimHistory(); Changed?.Invoke();
    }
    public void Redo()
    {
        if (InTransaction || !CanRedo) return;
        undo.Add(Capture()); var state = redo[^1]; redo.RemoveAt(redo.Count - 1);
        Restore(state); TrimHistory(); Changed?.Invoke();
    }
    private void ReconcileActive() { if (ActiveLayer is null) ActiveLayerId = Document.Layers.LastOrDefault()?.Id; if (ActiveLayer?.Mask is null) EditMask = false; }
    private void TrimHistory()
    {
        while (undo.Count + redo.Count > 100 || HistoryRetainedBytes > 256L * 1024 * 1024)
        {
            if (undo.Count > 0) undo.RemoveAt(0);
            else if (redo.Count > 0) redo.RemoveAt(0);
            else break;
        }
    }
    private static bool Equivalent(Document a, Document b) => ReferenceEquals(a, b) ||
        (a.Id == b.Id && a.Width == b.Width && a.Height == b.Height && a.Resolution == b.Resolution && a.Layers.SequenceEqual(b.Layers));
}
