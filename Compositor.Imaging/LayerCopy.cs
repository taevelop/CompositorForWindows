using Compositor.Core;
namespace Compositor.Imaging;
public static class LayerCopy
{
    public static Guid? ViaSelection(EditorSession session)
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit first.");
        if(session.ActiveLayer is not {} layer||layer.IsGroup||session.Document.Selection is {IsEmpty:true})return null;
        if(session.Document.Selection is null)return Duplicate(session);
        var copied=SelectionClipboardPixels.Copy(session.Document,layer.Id,session.EditMask);
        return copied is null?null:SelectionClipboardPixels.Paste(session,copied,true,"Layer via Copy");
    }
    public static Guid? Duplicate(EditorSession session)
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit first.");
        if(session.ActiveLayer is not {} layer)return null;
        var doc=session.Document;var ids=LayerHierarchy.Subtree(doc,layer.Id);
        if(doc.Layers.Length+ids.Count>10000)throw new InvalidDataException("Duplicating exceeds the 10,000 layer limit.");
        var mapping=ids.ToDictionary(id=>id,_=>Guid.NewGuid());
        var copies=doc.Layers.Where(l=>ids.Contains(l.Id)).Select(l=>l with{
            Id=mapping[l.Id],Name=l.Name+(l.Id==layer.Id?" copy":""),
            ParentId=l.ParentId is {} parent&&mapping.TryGetValue(parent,out var mapped)?mapped:l.ParentId
        }).ToArray();
        var next=doc with{Layers=doc.Layers.InsertRange(doc.Layers.IndexOf(layer)+1,copies)};
        next.Validate();var active=mapping[layer.Id];
        session.Apply(_=>{session.ActiveLayerId=active;return next;});return active;
    }
    /// <summary>Duplicates selected roots once, preserving order and sharing immutable image data.</summary>
    public static IReadOnlyDictionary<Guid,Guid> DuplicateSelected(EditorSession session)
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit first.");
        var doc=session.Document;var selected=session.SelectedLayerIds;
        var byId=doc.Layers.ToDictionary(l=>l.Id);
        bool HasSelectedAncestor(Layer layer)
        {
            for(var parent=layer.ParentId;parent is {} id;parent=byId[id].ParentId)
                if(selected.Contains(id))return true;
            return false;
        }
        var roots=doc.Layers.Where(l=>selected.Contains(l.Id)&&!HasSelectedAncestor(l)).ToArray();
        var ids=roots.SelectMany(l=>LayerHierarchy.Subtree(doc,l.Id)).ToHashSet();
        if(ids.Count==0)return new Dictionary<Guid,Guid>();
        if(doc.Layers.Length+ids.Count>10000)throw new InvalidDataException("Duplicating exceeds the 10,000 layer limit.");
        var map=ids.ToDictionary(id=>id,_=>Guid.NewGuid());
        var rootIds=roots.Select(l=>l.Id).ToHashSet();
        var output=System.Collections.Immutable.ImmutableArray.CreateBuilder<Layer>();
        foreach(var layer in doc.Layers)
        {
            output.Add(layer);
            if(!rootIds.Contains(layer.Id))continue;
            var subtree=LayerHierarchy.Subtree(doc,layer.Id);
            foreach(var source in doc.Layers.Where(l=>subtree.Contains(l.Id)))
                output.Add(source with{Id=map[source.Id],Name=source.Name+(source.Id==layer.Id?" copy":""),
                    ParentId=source.ParentId is {} parent&&map.TryGetValue(parent,out var mapped)?mapped:source.ParentId});
        }
        var next=doc with{Layers=output.ToImmutable()};next.Validate();
        session.Apply(_=>next);
        session.SelectLayers(roots.Select(l=>map[l.Id]),map[roots[^1].Id]);
        return map;
    }}
