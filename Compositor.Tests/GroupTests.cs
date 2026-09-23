using System.Collections.Immutable;
using System.Text.Json.Nodes;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;

public sealed class GroupTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "CompositorGroups-" + Guid.NewGuid().ToString("N"));
    public GroupTests() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        if (!Path.GetFullPath(root).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(root).StartsWith("CompositorGroups-")) throw new IOException("Unsafe cleanup.");
        Directory.Delete(root, true);
    }
    private static Layer Colored(string name, byte r, byte g, byte b, Guid? parent = null)
    {
        var pixels = new byte[64 * 64 * 4];
        for (int i = 0; i < pixels.Length; i += 4) { pixels[i] = r; pixels[i + 1] = g; pixels[i + 2] = b; pixels[i + 3] = 255; }
        return Layer.Blank(name, 64, 64) with { Pixels = Raster.FromRgba(64, 64, pixels), ParentId = parent };
    }
    private static byte[] Render(Document doc)
    {
        using var renderer = new CanvasRenderer(); using var image = renderer.Flatten(doc);
        using var bitmap = new SKBitmap(CanvasRenderer.Info(doc.Width, doc.Height));
        image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0); return bitmap.GetPixelSpan().ToArray();
    }

    [Fact]
    public void ArbitraryFlatStorageTraversesSiblingsAndInheritsVisibilityAndOpacity()
    {
        var outer = Layer.Group("Outer", 64, 64) with { Opacity = .5 };
        var inner = Layer.Group("Inner", 64, 64, outer.Id) with { Opacity = .5 };
        var red = Colored("Red", 255, 0, 0, inner.Id) with { Opacity = .5 };
        var bottom = Colored("Bottom", 0, 0, 255); var top = Colored("Top", 0, 255, 0);
        var doc = Document.Create(64, 64) with { Layers = [red, bottom, inner, outer, top] }; doc.Validate();
        var entries = LayerHierarchy.Entries(doc);
        Assert.Equal(new[] { bottom.Id, outer.Id, inner.Id, red.Id, top.Id }, entries.Select(e => e.Layer.Id));
        Assert.Equal(.125, entries.Single(e => e.Layer.Id == red.Id).Opacity);
        Assert.Equal(2, entries.Single(e => e.Layer.Id == red.Id).Depth);
        Assert.Equal(new[] { top.Id, outer.Id, inner.Id, red.Id, bottom.Id }, LayerHierarchy.Entries(doc, true).Select(e => e.Layer.Id));
        Assert.DoesNotContain(LayerHierarchy.Entries(doc, true, new HashSet<Guid> { outer.Id }), e => e.Layer.Id == red.Id);
        var hidden = doc.Replace(outer with { Visible = false });
        Assert.False(LayerHierarchy.Entries(hidden).Single(e => e.Layer.Id == red.Id).Visible); Assert.True(red.Visible);
    }

    [Fact]
    public void PassThroughOpacityAppliesToEachChildRatherThanIsolatedGroup()
    {
        var group = Layer.Group("Group", 64, 64) with { Opacity = .5 };
        var red = Colored("Red", 255, 0, 0, group.Id); var blue = Colored("Blue", 0, 0, 255, group.Id);
        var doc = Document.Create(64, 64) with { Layers = [group, red, blue] };
        var rgba = Render(doc);
        Assert.InRange(rgba[0], (byte)63, (byte)64); Assert.Equal((byte)128, rgba[2]); Assert.InRange(rgba[3], (byte)191, (byte)192);
        Assert.All(Render(doc.Replace(group with { Visible = false })), value => Assert.Equal((byte)0, value));
    }

    [Theory]
    [InlineData(BlendMode.Normal)][InlineData(BlendMode.Multiply)][InlineData(BlendMode.Screen)]
    public void NestedGroupMaskAndBlendEqualEquivalentFlatLayers(BlendMode mode)
    {
        var outer = Layer.Group("Outer", 64, 64) with { Opacity = .5 };
        var inner = Layer.Group("Inner", 64, 64, outer.Id) with { Opacity = .5 };
        var bottom = Colored("Background", 120, 200, 180);
        var child = Colored("Child", 220, 40, 100, inner.Id) with { Mask = LayerMask.Solid(1, 1, 128), Opacity = .5, Blend = mode };
        var doc = Document.Create(64, 64) with { Layers = [child, bottom, inner, outer] };
        var flat = doc with { Layers = [bottom, child with { ParentId = null, Opacity = .125 }] };
        Assert.Equal(Render(flat), Render(doc));
    }

    [Fact]
    public void WrapUngroupReorderAndReparentKeepSubtreesIntact()
    {
        var a = Colored("A", 255, 0, 0); var b = Colored("B", 0, 255, 0); var c = Colored("C", 0, 0, 255);
        var doc = Document.Create(64, 64) with { Layers = [a, b, c] }; var group = Layer.Group("Group", 64, 64);
        var wrapped = LayerHierarchy.Wrap(doc, b.Id, group); Assert.Equal(Render(doc), Render(wrapped));
        var moved = LayerHierarchy.Reorder(wrapped, group.Id, 1);
        Assert.Equal(new[] { a.Id, c.Id, b.Id }, LayerHierarchy.Entries(moved).Where(e => !e.Layer.IsGroup).Select(e => e.Layer.Id));
        moved = LayerHierarchy.Reparent(moved, c.Id, group.Id);
        Assert.Equal(new[] { b.Id, c.Id }, moved.Layers.Where(l => l.ParentId == group.Id).Select(l => l.Id));
        Assert.False(LayerHierarchy.CanReparent(moved, group.Id, b.Id));
        Assert.Throws<InvalidOperationException>(() => LayerHierarchy.Reparent(moved, group.Id, group.Id));
        var inner = Layer.Group("Inner", 64, 64, group.Id); moved = moved with { Layers = moved.Layers.Add(inner) };
        Assert.Throws<InvalidOperationException>(() => LayerHierarchy.Reparent(moved, group.Id, inner.Id));
        var ungrouped = LayerHierarchy.Ungroup(moved, group.Id);
        Assert.All(ungrouped.Layers, l => Assert.Null(l.ParentId)); Assert.Equal(Render(moved), Render(ungrouped));
        var deleted = LayerHierarchy.Delete(moved, group.Id); Assert.Single(deleted.Layers); Assert.Equal(a.Id, deleted.Layers[0].Id);
    }

    [Theory]
    [InlineData(true)][InlineData(false)]
    public void UngroupBakesOpacityAndVisibilityIntoDirectChildren(bool visible)
    {
        var outer = Layer.Group("Outer", 64, 64) with { Opacity = .5, Visible = visible };
        var inner = Layer.Group("Inner", 64, 64, outer.Id) with { Opacity = .5 };
        var child = Colored("Child", 200, 50, 100, inner.Id); var top = Colored("Top", 10, 180, 50, outer.Id) with { Opacity = .5 };
        var doc = Document.Create(64, 64) with { Layers = [child, outer, inner, top] };
        var ungrouped = LayerHierarchy.Ungroup(doc, outer.Id);
        Assert.Equal(Render(doc), Render(ungrouped)); Assert.Equal(.25, ungrouped.Layers.First(l => l.Id == inner.Id).Opacity);
        Assert.Equal(child, ungrouped.Layers.First(l => l.Id == child.Id));
    }

    [Fact]
    public void GroupMoveAndDeleteUndoPreservePixelsMasksAndSelection()
    {
        var group = Layer.Group("Group", 64, 64); var nested = Layer.Group("Nested", 64, 64, group.Id);
        var child = Colored("Child", 200, 80, 20, nested.Id) with { Mask = LayerMask.Solid(1, 1) };
        var doc = Document.Create(64, 64) with { Layers = [group, nested, child] }; var session = new EditorSession(doc) { ActiveLayerId = group.Id };
        session.Begin(); session.Preview(LayerHierarchy.Translate(doc, group.Id, 20, 15)); session.Preview(LayerHierarchy.Translate(doc, group.Id, 0, 0)); session.Commit();
        Assert.False(session.IsModified); Assert.False(session.CanUndo);
        session.Apply(d => LayerHierarchy.Translate(d, group.Id, 10, 20));
        var moved = session.Document.Layers.Single(l => l.Id == child.Id); Assert.Equal(10, moved.Transform.X); Assert.Equal(20, moved.Transform.Y);
        Assert.Same(child.Pixels, moved.Pixels); Assert.Same(child.Mask, moved.Mask);
        session.Apply(d => LayerHierarchy.Delete(d, group.Id)); Assert.Empty(session.Document.Layers);
        session.Undo(); Assert.Equal(group.Id, session.ActiveLayerId); Assert.Equal(3, session.Document.Layers.Length);
        session.Undo(); Assert.Same(doc, session.Document); session.Redo(); Assert.Equal(10, session.Document.Layers.Single(l => l.Id == child.Id).Transform.X);
    }

    [Theory]
    [InlineData("missing")][InlineData("nonGroup")][InlineData("cycle")][InlineData("self")][InlineData("depth")]
    public void InvalidHierarchyIsRejectedWithoutChangingSession(string kind)
    {
        var doc = Document.Create(16, 16); var group = Layer.Group("Group", 16, 16); var leaf = doc.Layers[0];
        var invalid = kind switch
        {
            "missing" => doc with { Layers = [leaf with { ParentId = Guid.NewGuid() }] },
            "nonGroup" => doc with { Layers = [leaf, group with { ParentId = leaf.Id }] },
            "self" => doc with { Layers = [group with { ParentId = group.Id }] },
            "cycle" => doc with { Layers = [group with { ParentId = leaf.Id }, leaf with { IsGroup = true, ParentId = group.Id }] },
            _ => DeepDocument(65)
        };
        var session = new EditorSession(doc); Assert.Throws<InvalidDataException>(() => session.Apply(_ => invalid)); Assert.Same(doc, session.Document); Assert.False(session.IsModified);
        DeepDocument(64).Validate();
    }
    private static Document DeepDocument(int count)
    {
        var layers = ImmutableArray.CreateBuilder<Layer>(); Guid? parent = null;
        for (int i = 0; i < count; i++) { var group = Layer.Group($"Group {i}", 16, 16, parent); layers.Add(group); parent = group.Id; }
        layers.Add(Layer.Blank("Leaf", 16, 16) with { ParentId = parent }); return Document.Create(16, 16) with { Layers = layers.ToImmutable() };
    }

    [Fact]
    public void GroupsRoundTripWithMasksAndSaveFailurePreservesOriginal()
    {
        var group = Layer.Group("Group", 64, 64) with { Opacity = .5 }; var child = Colored("Child", 120, 60, 20, group.Id) with { Mask = LayerMask.Solid(1, 1, 128) };
        var doc = Document.Create(64, 64) with { Layers = [child, group] }; string path = Path.Combine(root, "Groups.comp");
        ProjectStore.Save(doc, group.Id, path); var loaded = ProjectStore.Load(path);
        Assert.Equal(group.Id, loaded.ActiveLayerId); Assert.Equal(group.Id, loaded.Document.Layers[0].ParentId); Assert.True(loaded.Document.Layers[1].IsGroup);
        Assert.Equal(Render(doc), Render(loaded.Document)); Assert.Equal(2, Directory.GetFiles(Path.Combine(path, "images")).Length);
        var old = File.ReadAllBytes(Path.Combine(path, "manifest.json"));
        Assert.Throws<IOException>(() => ProjectStore.SaveInternal(doc.Replace(group with { Visible = false }), group.Id, path, () => throw new IOException("publish failure")));
        Assert.Equal(old, File.ReadAllBytes(Path.Combine(path, "manifest.json"))); Assert.Equal(Render(doc), Render(ProjectStore.Load(path).Document));
        string png = Path.Combine(root, "Composite.png"); ImageCodec.Export(doc, png, false); Assert.Equal(Render(doc), ImageCodec.Load(png).ToRgba());
    }

    [Theory]
    [InlineData(1, 1, false)][InlineData(2, 1, true)][InlineData(7, .5, false)][InlineData(8, .5, true)]
    public void GroupVersionRulesFollowMacFormat(int version, double opacity, bool accepted)
    {
        var group = Layer.Group("Folder", 16, 16) with { Opacity = opacity }; var doc = Document.Create(16, 16) with { Layers = [group] };
        string path = Path.Combine(root, "Version.comp"); ProjectStore.Save(doc, group.Id, path);
        string file = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(file))!; json["version"] = version;
        if (version == 2) json["layers"]![0]!.AsObject().Remove("opacity");
        File.WriteAllText(file, json.ToJsonString());
        if (accepted) Assert.True(ProjectStore.Load(path).Document.Layers[0].IsGroup);
        else Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path));
    }

    [Theory]
    [InlineData("image")][InlineData("mask")][InlineData("blend")][InlineData("parent")]
    public void InvalidOrUnsupportedGroupMetadataDoesNotGetOverwritten(string kind)
    {
        var group = Layer.Group("Folder", 16, 16); var doc = Document.Create(16, 16) with { Layers = [group] };
        string path = Path.Combine(root, "Invalid.comp"); ProjectStore.Save(doc, null, path);
        string file = Path.Combine(path, "manifest.json"); var json = JsonNode.Parse(File.ReadAllText(file))!; var l = json["layers"]![0]!;
        switch (kind) { case "image": l["imageFile"] = group.Id.ToString().ToUpperInvariant() + ".png"; break;
            case "mask": l["maskFile"] = group.Id.ToString().ToUpperInvariant() + ".mask.png"; break;
            case "blend": l["blendMode"] = "Multiply"; break; case "parent": l["parentID"] = Guid.NewGuid().ToString(); break; }
        File.WriteAllText(file, json.ToJsonString()); byte[] before = File.ReadAllBytes(file);
        if (kind is "mask" or "blend") { Assert.Throws<NotSupportedException>(() => ProjectStore.Load(path)); Assert.Throws<NotSupportedException>(() => ProjectStore.Save(doc, null, path)); }
        else { Assert.Throws<InvalidDataException>(() => ProjectStore.Load(path)); Assert.Throws<InvalidDataException>(() => ProjectStore.Save(doc, null, path)); }
        Assert.Equal(before, File.ReadAllBytes(file));
    }

    [Fact]
    public void IncrementalViewportTracksAncestorPropertiesReparentingAndGroupOrder()
    {
        var a = Layer.Group("A", 64, 64); var b = Layer.Group("B", 64, 64) with { Opacity = .4 };
        var red = Colored("Red", 255, 0, 0, a.Id); var blue = Colored("Blue", 0, 0, 255, b.Id);
        var doc = Document.Create(64, 64) with { Layers = [red, a, blue, b] };
        using var viewport = new ViewportRenderer();
        void Compare(Document d)
        {
            using var image = viewport.Render(d, 64, 64, 1, 0, 0); using var bitmap = new SKBitmap(CanvasRenderer.Info(64, 64));
            image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0); Assert.Equal(Render(d), bitmap.GetPixelSpan().ToArray());
        }
        Compare(doc); doc = doc.Replace(a with { Opacity = .25 }); Compare(doc);
        doc = doc.Replace(b with { Visible = false }); Compare(doc);
        doc = LayerHierarchy.Reparent(doc, red.Id, b.Id); Compare(doc);
        doc = doc.Replace(b); Compare(doc); doc = LayerHierarchy.Reorder(doc, a.Id, 1); Compare(doc);
        doc = LayerHierarchy.Translate(doc, b.Id, 10, 8); Compare(doc);
        doc = LayerHierarchy.Ungroup(doc, b.Id); Compare(doc);
    }
}
