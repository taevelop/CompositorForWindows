using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;

public class TextEditTests
{
    private static Raster Pixels(int width, int height) => ShapeRaster.Create(new(ShapeKind.Rectangle, .2, .4, .8), width, height);
    private static Document Existing(LayerTransform? transform = null)
    {
        var doc = Document.Create(300, 200);
        return doc.Replace(doc.Layers[0] with { Pixels = Pixels(80, 40), Transform = transform ?? new(12, 16, 160, 60), Text = new("old", "missing-font", 32), Mask = LayerMask.Solid(80, 40), Opacity = .6, Blend = BlendMode.Screen });
    }

    [Fact]
    public void NewTextInGroupKeepsSelectionAndHasOneUndo()
    {
        var doc = Document.Create(300, 200);
        var group = Layer.Group("Group", 300, 200); doc = doc with { Layers = doc.Layers.Add(group), Selection = new("M2,3 L90,3 L90,100 L2,100 Z") };
        var session = new EditorSession(doc);session.ActiveLayerId = group.Id;
        var style = new LayerTextStyle("  Hello\n  👩‍💻 world  ", "Segoe UI", 32);
        var request = TextEdit.Capture(session, new(doc.Id, null, new(20, 30), style));
        var prepared = TextEdit.Prepare(request, Pixels(120, 60));
        Assert.Same(doc, session.Document);Assert.Equal(0, session.UndoCount);
        Assert.True(prepared.Apply(session));var layer = session.ActiveLayer!;
        Assert.Equal("Hello 👩‍💻 world", layer.Name);Assert.Equal(group.Id, layer.ParentId);Assert.Equal(style, layer.Text);
        Assert.Equal(new LayerTransform(20, 30, 120, 60), layer.Transform);Assert.Same(doc.Selection, session.Document.Selection);
        Assert.Equal(1, session.UndoCount);Assert.False(session.EditMask);
        session.Undo();Assert.Same(doc, session.Document);Assert.Equal(group.Id, session.ActiveLayerId);
        session.Redo();Assert.Same(prepared.Document, session.Document);Assert.Equal(prepared.LayerId, session.ActiveLayerId);
    }

    [Theory]
    [InlineData(0, false, false)]
    [InlineData(37, false, false)]
    [InlineData(-29, true, false)]
    [InlineData(120, true, true)]
    public void EditPreservesTransformedAnchorScaleAndMask(double angle, bool flipX, bool flipY)
    {
        var transform = new LayerTransform(12, 16, 160, 60, angle, flipX, flipY, Sampling.Smooth);
        var doc = Existing(transform);var layer = doc.Layers[0];var session = new EditorSession(doc);
        var style = layer.Text! with { Content = "new 😀", Tracking = 2 };
        var prepared = TextEdit.Prepare(TextEdit.Capture(session, new(doc.Id, layer.Id, new(999, 999), style)), Pixels(120, 80));
        var next = prepared.Document.Layers[0];var beforeAnchor = transform.ToDocument(new(0, 0), 80, 40);var afterAnchor = next.Transform.ToDocument(new(0, 0), 120, 80);
        Assert.Equal(beforeAnchor.X, afterAnchor.X, 8);Assert.Equal(beforeAnchor.Y, afterAnchor.Y, 8);
        Assert.Equal(240, next.Transform.Width);Assert.Equal(120, next.Transform.Height);Assert.Equal(angle, next.Transform.Rotation);
        Assert.Equal(flipX, next.Transform.FlipX);Assert.Equal(flipY, next.Transform.FlipY);Assert.Equal(Sampling.Smooth, next.Transform.Sampling);
        Assert.Same(layer.Mask!.Pixels, next.Mask!.Pixels);Assert.Equal(transform, next.Mask.Placement);Assert.Equal(layer.Opacity, next.Opacity);Assert.Equal(layer.Blend, next.Blend);Assert.Equal(layer.Name, next.Name);
        Assert.True(prepared.Apply(session));Assert.Equal(style, session.ActiveLayer!.Text);session.Undo();Assert.Same(layer, session.ActiveLayer);session.Redo();Assert.Same(next, session.ActiveLayer);
    }

    [Fact]
    public void ExplicitParagraphResizeKeepsDraftTransformAndIndependentMask()
    {
        var doc = Existing();var layer = doc.Layers[0];var maskPlacement = new LayerTransform(-20, 40, 80, 40, 13);
        layer = layer with { Mask = layer.Mask! with { Placement = maskPlacement, Linked = false } };doc = doc.Replace(layer);
        var session = new EditorSession(doc);var style = layer.Text! with { Content = "paragraph", BoxSize = new(100.2, 80.2) };
        var draftTransform = new LayerTransform(40, 60, 240, 180, 17, true);
        var request = TextEdit.Capture(session, new(doc.Id, layer.Id, new(0, 0), style, draftTransform));
        Assert.Throws<InvalidDataException>(() => TextEdit.Prepare(request, Pixels(100, 80)));
        var prepared = TextEdit.Prepare(request, Pixels(101, 81));var edited = prepared.Document.Layers[0];
        Assert.Equal(draftTransform, edited.Transform);Assert.Same(layer.Mask, edited.Mask);
    }

    [Fact]
    public void NoChangeAndBlankNewDraftDoNotRenderOrTouchHistory()
    {
        var doc = Existing();var session = new EditorSession(doc);var layer = doc.Layers[0];
        foreach (var draft in new[] { new TextDraft(doc.Id, layer.Id, new(0, 0), layer.Text!), new TextDraft(doc.Id, null, new(0, 0), new(" \n\t")) })
        {
            var request = TextEdit.Capture(session, draft);Assert.False(request.NeedsRender);
            Assert.False(TextEdit.Prepare(request, null).Apply(session));Assert.Same(doc, session.Document);
        }
        Assert.False(session.IsModified);Assert.Equal(0, session.UndoCount);
    }

    [Fact]
    public void LateResultsRejectGenerationSelectionAndOtherSession()
    {
        var doc = Existing();var session = new EditorSession(doc);
        TextEdit.Prepared Prepare() => TextEdit.Prepare(TextEdit.Capture(session, new(doc.Id, doc.Layers[0].Id, new(0, 0), new("changed"))), Pixels(100, 60));
        var prepared = Prepare();Assert.Throws<InvalidOperationException>(() => prepared.Apply(new EditorSession(doc)));
        session.Begin();session.Cancel();Assert.Same(doc, session.Document);
        Assert.Throws<InvalidOperationException>(() => prepared.Apply(session));
        prepared = Prepare();session.ActiveLayerId = null;Assert.Throws<InvalidOperationException>(() => prepared.Apply(session));
        Assert.Same(doc, session.Document);Assert.Equal(0, session.UndoCount);
    }

    [Fact]
    public void CancelAndInvalidTargetsLeaveOriginalUntouched()
    {
        var doc = Existing();var session = new EditorSession(doc);
        Assert.Throws<InvalidOperationException>(() => TextEdit.Capture(session, new(Guid.NewGuid(), null, new(0, 0), new())));
        Assert.Throws<InvalidOperationException>(() => TextEdit.Capture(session, new(doc.Id, Guid.NewGuid(), new(0, 0), new())));
        Assert.Throws<InvalidDataException>(() => TextEdit.Capture(session, new(doc.Id, null, new(double.NaN, 0), new())));
        var request = TextEdit.Capture(session, new(doc.Id, doc.Layers[0].Id, new(0, 0), new("changed")));
        Assert.Throws<OperationCanceledException>(() => TextEdit.Prepare(request, null, new(true)));
        var prepared = TextEdit.Prepare(request, Pixels(100, 60));Assert.Throws<OperationCanceledException>(() => prepared.Apply(session, new(true)));
        var rasterized = doc.Replace(doc.Layers[0] with { Pixels = Pixels(80, 40) });session.Load(rasterized);
        Assert.Throws<InvalidOperationException>(() => TextEdit.Capture(session, new(doc.Id, doc.Layers[0].Id, new(0, 0), new())));
        Assert.Throws<InvalidOperationException>(() => prepared.Apply(session));Assert.Same(rasterized, session.Document);Assert.Equal(0, session.UndoCount);
    }

    [Fact]
    public void NewLayerRejectsSourceBudgetBeforeMutation()
    {
        var doc = Document.Create(10000, 10000);doc = doc.Replace(doc.Layers[0] with { Mask = LayerMask.Solid(1, 1) });
        var session = new EditorSession(doc);var request = TextEdit.Capture(session, new(doc.Id, null, new(0, 0), new("text")));
        Assert.Throws<InvalidDataException>(() => TextEdit.Prepare(request, Pixels(40, 20)));
        Assert.Same(doc, session.Document);Assert.Equal(0, session.UndoCount);
    }

    [Fact]
    public void ReloadingSameDocumentInvalidatesPreparedText()
    {
        var doc = Existing();var session = new EditorSession(doc);
        var prepared = TextEdit.Prepare(TextEdit.Capture(session, new(doc.Id, doc.Layers[0].Id, new(0, 0), new("new"))), Pixels(100, 60));
        session.Load(doc);Assert.Same(doc, session.Document);
        Assert.Throws<InvalidOperationException>(() => prepared.Apply(session));Assert.Equal(0, session.UndoCount);
    }

    [Fact]
    public void ParagraphStyleChangeWithoutResizeKeepsExistingPixelScale()
    {
        var doc = Existing();var session = new EditorSession(doc);var layer = doc.Layers[0];
        var style = layer.Text! with { Content = "paragraph", BoxSize = new(100, 80) };
        var prepared = TextEdit.Prepare(TextEdit.Capture(session, new(doc.Id, layer.Id, new(0, 0), style)), Pixels(100, 80));
        Assert.Equal(200, prepared.Document.Layers[0].Transform.Width);Assert.Equal(120, prepared.Document.Layers[0].Transform.Height);
        Assert.Equal(new PointD(12, 16), prepared.Document.Layers[0].Transform.ToDocument(new(0, 0), 100, 80));
    }

    [Fact]
    public void LayerNameTruncatesGraphemesWithoutSplittingEmoji()
    {
        var session = new EditorSession(Document.Create(100, 100));string content = string.Concat(Enumerable.Repeat("👩‍💻", 45));
        var request = TextEdit.Capture(session, new(session.Document.Id, null, new(0, 0), new(content)));
        var prepared = TextEdit.Prepare(request, Pixels(40, 20));
        Assert.Equal(string.Concat(Enumerable.Repeat("👩‍💻", 40)), prepared.Document.Layers.Last().Name);
    }
}
