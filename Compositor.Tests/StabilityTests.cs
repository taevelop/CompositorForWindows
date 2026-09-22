using Compositor.Core;
using Xunit;

namespace Compositor.Tests;

public sealed class StabilityTests
{
    [Fact]
    public void EquivalentEditPreservesCleanStateAndRedo()
    {
        var original = Document.Create(32, 32); var session = new EditorSession(original);
        session.Apply(d => d.Replace(d.Layers[0] with { Name = "Renamed" })); session.Undo();
        Assert.True(session.CanRedo);
        session.Apply(d => d.Replace(d.Layers[0] with { Transform = d.Layers[0].Transform with { X = 0 } }));
        Assert.False(session.IsModified); Assert.False(session.CanUndo); Assert.True(session.CanRedo);
        Assert.Same(original, session.Document);
    }

    [Fact]
    public void MoveAwayAndBackIsNotAnEdit()
    {
        var original = Document.Create(32, 32); var session = new EditorSession(original);
        session.Begin(); session.Preview(original.Replace(original.Layers[0] with { Transform = original.Layers[0].Transform with { X = 15 } }));
        Assert.True(session.IsModified);
        session.Preview(original.Replace(original.Layers[0] with { Transform = original.Layers[0].Transform with { X = 0 } })); session.Commit();
        Assert.False(session.IsModified); Assert.False(session.CanUndo); Assert.Same(original, session.Document);
    }

    [Fact]
    public void UndoDeleteRestoresSelectedLayer()
    {
        var document = Document.Create(32, 32); var second = Layer.Blank("Second", 32, 32);
        document = document with { Layers = document.Layers.Add(second) };
        var session = new EditorSession(document) { ActiveLayerId = second.Id };
        session.Apply(d => d with { Layers = d.Layers.Remove(second) });
        Assert.NotNull(session.ActiveLayer); Assert.NotEqual(second.Id, session.ActiveLayerId);
        session.Undo();
        Assert.Equal(second.Id, session.ActiveLayerId);
    }
}
