using System.Collections.Immutable;
using Compositor.Core;
namespace Compositor.Imaging;
/// <summary>A fully validated copy/drop proposal; preparing it does not mutate a session.</summary>
public sealed record LayerCopyPlacement(Document Document,ImmutableArray<Guid> Roots,ImmutableDictionary<Guid,Guid> Mapping)
{
    public static LayerCopyPlacement Create(Document source,IEnumerable<Guid> selection,Guid? parent,Guid? above=null,bool atBottom=false)
    {
        var roots=LayerHierarchy.SelectedRoots(source,selection);
        if(roots.Length==0)throw new InvalidOperationException("Select layers to copy.");
        if(roots.Any(l=>!LayerHierarchy.CanReparent(source,l.Id,parent)))throw new InvalidOperationException("A group cannot be copied into itself or its descendants.");
        var rootIds=roots.Select(l=>l.Id).ToHashSet();
        var ids=roots.SelectMany(l=>LayerHierarchy.Subtree(source,l.Id)).ToHashSet();
        if(source.Layers.Length+ids.Count>10000)throw new InvalidDataException("Duplicating exceeds the 10,000 layer limit.");
        var map=ids.ToImmutableDictionary(id=>id,_=>Guid.NewGuid());
        var copies=source.Layers.Where(l=>ids.Contains(l.Id)).Select(l=>l with{
            Id=map[l.Id],Name=l.Name+(rootIds.Contains(l.Id)?" copy":""),
            ParentId=l.ParentId is {} old&&map.TryGetValue(old,out var mapped)?mapped:l.ParentId
        });
        var copiedRoots=roots.Select(l=>map[l.Id]).ToImmutableArray();
        var combined=source with{Layers=source.Layers.AddRange(copies)};
        var next=LayerHierarchy.PlaceSelected(combined,copiedRoots,parent,above,atBottom);
        return new(next,copiedRoots,map);
    }
}