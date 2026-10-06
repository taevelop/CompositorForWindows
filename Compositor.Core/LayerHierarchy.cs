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
    /// <summary>Wraps selected roots at their closest common parent, matching the original editor.</summary>
    public static Document WrapSelected(Document doc, IEnumerable<Guid> selection, Layer group)
    {
        doc.Validate();
        if (!group.IsGroup || doc.Layers.Any(l => l.Id == group.Id)) throw new InvalidOperationException("Invalid new group.");
        var byId = doc.Layers.ToDictionary(l => l.Id);
        var selected = selection.ToHashSet();
        if (selected.Count == 0 || selected.Any(id => !byId.ContainsKey(id))) throw new InvalidOperationException("Select existing layers to group.");
        List<Guid?> Ancestors(Guid id)
        {
            var result = new List<Guid?>();
            for (var parent = byId[id].ParentId; parent is {} p; parent = byId[p].ParentId) result.Add(p);
            result.Add(null); return result;
        }
        var roots = selected.Where(id => !Ancestors(id).Any(p => p is {} parent && selected.Contains(parent))).ToHashSet();
        var ordered = Entries(doc).Select(e => e.Layer.Id).Where(roots.Contains).ToArray();
        Guid? common = Ancestors(ordered[0]).First(candidate => ordered.All(id => Ancestors(id).Contains(candidate)));
        var branches = ordered.Select(id =>
        {
            while (byId[id].ParentId is {} parent && parent != common) id = parent;
            return id;
        }).ToHashSet();
        int highest = -1;
        for (int i = 0; i < doc.Layers.Length; i++) if (branches.Contains(doc.Layers[i].Id)) highest = i;
        int insertion = doc.Layers.Take(highest + 1).Count(l => !roots.Contains(l.Id));
        var layers = doc.Layers.Where(l => !roots.Contains(l.Id)).ToList();
        layers.Insert(insertion, group with { ParentId = common });
        layers.AddRange(ordered.Select(id => byId[id] with { ParentId = group.Id }));
        return Normalize(doc with { Layers = layers.ToImmutableArray() });
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
    public static Layer[] SelectedRoots(Document doc, IEnumerable<Guid> selection)
    {
        doc.Validate();
        var selected = selection.ToHashSet(); var byId = doc.Layers.ToDictionary(l => l.Id);
        if (selected.Any(id => !byId.ContainsKey(id))) throw new InvalidOperationException("Layer no longer exists.");
        bool IsRoot(Layer layer)
        {
            for (var parent = layer.ParentId; parent is {} id; parent = byId[id].ParentId)
                if (selected.Contains(id)) return false;
            return true;
        }
        return Entries(doc).Select(e => e.Layer).Where(l => selected.Contains(l.Id) && IsRoot(l)).ToArray();
    }
    public static Document ReparentSelected(Document doc, IEnumerable<Guid> selection, Guid? parent)
    {
        var roots = SelectedRoots(doc, selection);
        if (roots.Length == 0) return doc;
        if (roots.Any(l => !CanReparent(doc, l.Id, parent))) throw new InvalidOperationException("A group cannot be moved into itself or its descendants.");
        // Keep the existing no-op behavior when all selected roots already have this parent.
        if (roots.All(l => l.ParentId == parent)) return doc;
        var ids = roots.Select(l => l.Id).ToHashSet();
        var layers = doc.Layers.Where(l => !ids.Contains(l.Id)).Concat(roots.Select(l => l with { ParentId = parent }));
        return Normalize(doc with { Layers = layers.ToImmutableArray() });
    }
    public static Document MoveSelectedOut(Document doc, IEnumerable<Guid> selection)
    {
        var roots = SelectedRoots(doc, selection).Where(l => l.ParentId is not null).ToArray();
        if (roots.Length == 0) return doc;
        var byId = doc.Layers.ToDictionary(l => l.Id); var ids = roots.Select(l => l.Id).ToHashSet();
        var layers = doc.Layers.Where(l => !ids.Contains(l.Id)).ToList();
        foreach (var siblings in roots.GroupBy(l => l.ParentId!.Value))
        {
            var parent = byId[siblings.Key];
            int index = layers.FindIndex(l => l.Id == parent.Id);
            layers.InsertRange(index + 1, siblings.Select(l => l with { ParentId = parent.ParentId }));
        }
        return Normalize(doc with { Layers = layers.ToImmutableArray() });
    }

    /// <summary>Moves selected roots to a list drop slot. Array order is bottom to top.</summary>
    public static Document PlaceSelected(Document doc, IEnumerable<Guid> selection, Guid? parent, Guid? above = null, bool atBottom = false)
    {
        var roots = SelectedRoots(doc, selection);
        if (roots.Length == 0) return doc;
        if (above is not null && atBottom) throw new ArgumentException("A drop cannot have both an anchor and a bottom position.");
        if (roots.Any(l => !CanReparent(doc, l.Id, parent))) throw new InvalidOperationException("A group cannot be moved into itself or its descendants.");
        var carried = roots.SelectMany(l => Subtree(doc, l.Id)).ToHashSet();
        if (above is {} anchor && (carried.Contains(anchor) || !doc.Layers.Any(l => l.Id == anchor && l.ParentId == parent)))
            throw new InvalidOperationException("Invalid layer drop anchor.");
        var ids = roots.Select(l => l.Id).ToHashSet();
        var layers = doc.Layers.Where(l => !ids.Contains(l.Id)).ToList();
        int insertion = above is {} target ? layers.FindIndex(l => l.Id == target) + 1 : atBottom ? 0 : layers.Count;
        layers.InsertRange(insertion, roots.Select(l => l.ParentId == parent ? l : l with { ParentId = parent }));
        var next = Normalize(doc with { Layers = layers.ToImmutableArray() });
        return Entries(doc).Select(e => e.Layer).SequenceEqual(next.Layers) ? doc : next;
    }
    /// <summary>Moves selected roots one sibling step without reversing their relative order.</summary>
    public static Document ReorderSelected(Document doc, IEnumerable<Guid> selection, int direction)
    {
        if (direction == 0) return doc;
        if (direction is not (-1 or 1)) throw new ArgumentOutOfRangeException(nameof(direction));
        var ids = SelectedRoots(doc, selection).Select(l => l.Id).ToHashSet();
        var layers = doc.Layers.ToArray(); bool changed = false;
        foreach (var siblings in doc.Layers.GroupBy(l => l.ParentId))
        {
            var slots = Enumerable.Range(0, layers.Length).Where(i => layers[i].ParentId == siblings.Key).ToArray();
            int start = direction > 0 ? slots.Length - 2 : 1;
            for (int i = start; direction > 0 ? i >= 0 : i < slots.Length; i -= direction)
            {
                int a = slots[i], b = slots[i + direction];
                if (!ids.Contains(layers[a].Id) || ids.Contains(layers[b].Id)) continue;
                (layers[a], layers[b]) = (layers[b], layers[a]); changed = true;
            }
        }
        return changed ? Normalize(doc with { Layers = layers.ToImmutableArray() }) : doc;
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
