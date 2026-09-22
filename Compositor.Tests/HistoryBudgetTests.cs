using System.Collections.Immutable;
using Compositor.Core;
using Xunit;

namespace Compositor.Tests;

public sealed class HistoryBudgetTests
{
    [Fact]
    public void SavedRevisionTracksUndoRedoAndNewBranch()
    {
        var session = new EditorSession(Document.Create(32, 32));
        session.Apply(d => d.Replace(d.Layers[0] with { Name = "Saved" })); session.MarkSaved();
        session.Undo(); Assert.True(session.IsModified);
        session.Redo(); Assert.False(session.IsModified);
        session.Undo(); session.Apply(d => d.Replace(d.Layers[0] with { Name = "Branch" }));
        Assert.True(session.IsModified); Assert.False(session.CanRedo);
        session.Undo(); Assert.True(session.IsModified);
    }

    [Fact]
    public void CancelRestoresSelectionAndSavedState()
    {
        var doc = Document.Create(32, 32); var second = Layer.Blank("Second", 32, 32);
        doc = doc with { Layers = doc.Layers.Add(second) };
        var session = new EditorSession(doc) { ActiveLayerId = second.Id };
        session.Begin(); session.Preview(doc with { Layers = doc.Layers.Remove(second) });
        session.ActiveLayerId = doc.Layers[0].Id;
        Assert.Throws<InvalidOperationException>(() => session.MarkSaved());
        session.Cancel(); Assert.Same(doc, session.Document);
        Assert.Equal(second.Id, session.ActiveLayerId); Assert.False(session.IsModified);
    }

    [Fact]
    public void RecoveryRemainsDirtyUntilExplicitSave()
    {
        var session = new EditorSession(Document.Create(32, 32));
        session.Load(session.Document, recovered: true); Assert.True(session.IsModified);
        session.Apply(d => d.Replace(d.Layers[0] with { Name = "Edit" }));
        session.Undo(); Assert.True(session.IsModified);
        session.MarkSaved(); Assert.False(session.IsModified);
    }

    [Fact]
    public void MetadataHistoryRetainsOnlyOneHundredStates()
    {
        var session = new EditorSession(Document.Create(32, 32));
        for (int i = 0; i < 140; i++) session.Apply(d => d.Replace(d.Layers[0] with { Name = $"Edit {i}" }));
        Assert.Equal(100, session.UndoCount); Assert.Equal(0, session.HistoryRetainedBytes);
        for (int i = 0; i < 100; i++) session.Undo();
        Assert.False(session.CanUndo); Assert.Equal(100, session.RedoCount);
        Assert.Equal("Edit 39", session.Document.Layers[0].Name);
        for (int i = 0; i < 100; i++) session.Redo();
        Assert.Equal("Edit 139", session.Document.Layers[0].Name);
    }

    [Fact]
    public void PixelHistoryIsBoundedAcrossUndoAndRedo()
    {
        var session = new EditorSession(Document.Create(1024, 1024));
        var bytes = new byte[PixelTile.ByteCount]; bytes[3] = 255;
        for (int i = 0; i < 70; i++)
        {
            var tiles = ImmutableDictionary.CreateBuilder<TileKey, PixelTile>();
            for (int y = 0; y < 4; y++) for (int x = 0; x < 4; x++) tiles[new(x, y)] = new(bytes);
            var raster = new Raster(1024, 1024, tiles.ToImmutable());
            session.Apply(d => d.Replace(d.Layers[0] with { Pixels = raster }));
            Assert.InRange(session.HistoryRetainedBytes, 0, 256L * 1024 * 1024);
        }
        Assert.InRange(session.UndoCount, 1, 65);
        for (int i = 0; i < 20; i++) session.Undo();
        Assert.InRange(session.HistoryRetainedBytes, 0, 256L * 1024 * 1024);
        for (int i = 0; i < 20; i++) session.Redo();
        Assert.InRange(session.HistoryRetainedBytes, 0, 256L * 1024 * 1024);
        session.Load(Document.Create(32, 32));
        Assert.Equal(0, session.HistoryRetainedBytes); Assert.False(session.CanUndo); Assert.False(session.CanRedo);
    }
}
