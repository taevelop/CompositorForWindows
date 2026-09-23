using System.Collections.Immutable;

namespace Compositor.Core;

/// <summary>Sibling order follows the flat file array; transforms are absolute document coordinates.</summary>
public static class LayerHierarchy
{
    public sealed record Entry(Layer Layer, int Depth, bool Visible, double Opacity);

    public static void Validate(ImmutableArray<Layer> layers)
    {
        var byId = new Dictionary<Guid, Layer>();
        foreach (var layer in layers)
        {
            if (!byId.TryAdd(layer.Id, layer)) throw new InvalidDataException("Duplicate layer ID.");
            if (layer.IsGroup && (layer.Pixels.Tiles.Count != 0 || layer.Mask is not null || layer.Blend != BlendMode.Normal))
                throw new NotSupportedException("Groups must be pass-through folders without pixels or masks.");
        }
        foreach (var layer in layers)
        {
            var seen = new HashSet<Guid> { layer.Id }; var parent = layer.ParentId;
            while (parent is Guid id)
            {
                if (seen.Count > 64 || !seen.Add(id) || !byId.TryGetValue(id, out var group) || !group.IsGroup)
                    throw new InvalidDataException("Invalid layer parent, cycle, or hierarchy depth.");
                parent = group.ParentId;
            }
            if (layer.IsGroup && seen.Count > 64) throw new InvalidDataException("At most 64 nested groups are supported.");
        }
    }

    public static IReadOnlyList<Entry> Entries(Document document, bool topFirst = false, ISet<Guid>? collapsed = null)
    {
        var children = document.Layers.ToLookup(l => l.ParentId);
        var result = new List<Entry>(document.Layers.Length);
        void Visit(Guid? parent, int depth, bool visible, double opacity)
        {
            if (depth > 64) throw new InvalidDataException("Hierarchy too deep.");
            var siblings = topFirst ? children[parent].Reverse() : children[parent];
            foreach (var layer in siblings)
            {
                bool effectiveVisible = visible && layer.Visible; double effectiveOpacity = opacity * layer.Opacity;
                result.Add(new(layer, depth, effectiveVisible, effectiveOpacity));
                if (layer.IsGroup && collapsed?.Contains(layer.Id) != true) Visit(layer.Id, depth + 1, effectiveVisible, effectiveOpacity);
            }
        }
        Visit(null, 0, true, 1); return result;
    }

    public static HashSet<Guid> Subtree(Document doc, Guid id)
    {
        if (!doc.Layers.Any(l => l.Id == id)) throw new InvalidOperationException("Layer no longer exists.");
        var children = doc.Layers.ToLookup(l => l.ParentId);
        var result = new HashSet<Guid> { id }; var pending = new Stack<Guid>(); pending.Push(id);
        while (pending.TryPop(out var parent))
            foreach (var child in children[parent]) if (result.Add(child.Id)) pending.Push(child.Id);
        return result;
    }
    private static Document Normalize(Document document)
    {
        document.Validate(); return document with { Layers = Entries(document).Select(e => e.Layer).ToImmutableArray() };
    }
    public static Document Wrap(Document doc, Guid id, Layer group)
    {
        var layer = doc.Layers.First(l => l.Id == id);
        if (!group.IsGroup || doc.Layers.Any(l => l.Id == group.Id)) throw new InvalidOperationException("Invalid new group.");
        int index = doc.Layers.IndexOf(layer);
        return Normalize(doc with { Layers = doc.Layers.SetItem(index, layer with { ParentId = group.Id }).Insert(index, group with { ParentId = layer.ParentId }) });
    }
    public static Document Delete(Document doc, Guid id)
    {
        var remove = Subtree(doc, id); return Normalize(doc with { Layers = doc.Layers.Where(l => !remove.Contains(l.Id)).ToImmutableArray() });
    }
    public static Document Ungroup(Document doc, Guid id)
    {
        doc = Normalize(doc); var group = doc.Layers.First(l => l.Id == id);
        if (!group.IsGroup) throw new InvalidOperationException("Select a group to ungroup.");
        // Preserve appearance: direct children inherit the removed folder's own visibility and opacity.
        return Normalize(doc with { Layers = doc.Layers.Where(l => l.Id != id).Select(l => l.ParentId == id
            ? l with { ParentId = group.ParentId, Visible = l.Visible && group.Visible, Opacity = l.Opacity * group.Opacity } : l).ToImmutableArray() });
    }
    public static bool CanReparent(Document doc, Guid id, Guid? parent)
    {
        if (!doc.Layers.Any(l => l.Id == id)) return false;
        if (parent is null) return true;
        return doc.Layers.Any(l => l.Id == parent && l.IsGroup) && !Subtree(doc, id).Contains(parent.Value);
    }
    public static Document Reparent(Document doc, Guid id, Guid? parent)
    {
        if (!CanReparent(doc, id, parent)) throw new InvalidOperationException("A group cannot be moved into itself or its descendants.");
        var layer = doc.Layers.First(l => l.Id == id); if (layer.ParentId == parent) return doc;
        return Normalize(doc with { Layers = doc.Layers.Remove(layer).Add(layer with { ParentId = parent }) });
    }
    public static Document Reorder(Document doc, Guid id, int offset)
    {
        var layer = doc.Layers.First(l => l.Id == id); var siblings = doc.Layers.Where(l => l.ParentId == layer.ParentId).ToArray();
        int index = Array.IndexOf(siblings, layer), target = index + offset;
        if (target < 0 || target >= siblings.Length) return doc;
        int a = doc.Layers.IndexOf(layer), b = doc.Layers.IndexOf(siblings[target]);
        return Normalize(doc with { Layers = doc.Layers.SetItem(a, siblings[target]).SetItem(b, layer) });
    }
    public static Document Translate(Document doc, Guid id, double dx, double dy)
    {
        if (!double.IsFinite(dx) || !double.IsFinite(dy)) throw new InvalidDataException("Invalid movement.");
        if (dx == 0 && dy == 0) return doc;
        var ids = Subtree(doc, id);
        var result = doc with { Layers = doc.Layers.Select(l => ids.Contains(l.Id) ? l with { Transform = l.Transform with { X = l.Transform.X + dx, Y = l.Transform.Y + dy } } : l).ToImmutableArray() };
        result.Validate(); return result;
    }
    public static LayerTransform Bounds(Document doc, Guid id)
    {
        var ids = Subtree(doc, id);
        var points = doc.Layers.Where(l => ids.Contains(l.Id) && !l.IsGroup).SelectMany(l => new[]
        {
            l.Transform.ToDocument(new(0, 0), l.Pixels.Width, l.Pixels.Height),
            l.Transform.ToDocument(new(l.Pixels.Width, 0), l.Pixels.Width, l.Pixels.Height),
            l.Transform.ToDocument(new(0, l.Pixels.Height), l.Pixels.Width, l.Pixels.Height),
            l.Transform.ToDocument(new(l.Pixels.Width, l.Pixels.Height), l.Pixels.Width, l.Pixels.Height)
        }).ToArray();
        if (points.Length == 0) return doc.Layers.First(l => l.Id == id).Transform;
        double x = points.Min(p => p.X), y = points.Min(p => p.Y);
        return new(x, y, Math.Max(1, points.Max(p => p.X) - x), Math.Max(1, points.Max(p => p.Y) - y));
    }
}
