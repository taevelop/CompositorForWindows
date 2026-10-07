using System.Collections.Immutable;
using Compositor.Core;
namespace Compositor.Imaging;
/// <summary>Copies selected subtrees into a different document without changing either input.</summary>
public static class DocumentLayerTransfer
{
    public static LayerCopyPlacement Create(Document source,Document destination,IEnumerable<Guid> selection,Guid anchorId,PointD? point=null)
    {
        source.Validate();destination.Validate();
        var roots=LayerHierarchy.SelectedRoots(source,selection);
        if(roots.Length==0)throw new InvalidOperationException("Select layers to copy.");
        var included=roots.SelectMany(l=>LayerHierarchy.Subtree(source,l.Id)).ToHashSet();
        if(!included.Contains(anchorId))throw new InvalidOperationException("The anchor must belong to the copied layers.");
        if(destination.Layers.Length+included.Count>10000)throw new InvalidDataException("Copying exceeds the 10,000 layer limit.");
        var anchor=source.Layers.Single(l=>l.Id==anchorId).Transform;
        var center=point??new PointD(destination.Width/2d,destination.Height/2d);
        double dx=center.X-anchor.X-anchor.Width/2,dy=center.Y-anchor.Y-anchor.Height/2;
        if(!double.IsFinite(dx)||!double.IsFinite(dy))throw new InvalidDataException("Invalid copy destination.");
        var map=included.ToImmutableDictionary(id=>id,_=>Guid.NewGuid());
        var copies=source.Layers.Where(l=>included.Contains(l.Id)).Select(l=>l with
        {
            Id=map[l.Id],ParentId=l.ParentId is {} parent&&map.TryGetValue(parent,out var copiedParent)?copiedParent:null,
            Transform=l.Transform with{X=l.Transform.X+dx,Y=l.Transform.Y+dy}
        });
        var next=destination with{Layers=destination.Layers.AddRange(copies)};
        next.Validate();
        return new(next,roots.Select(l=>map[l.Id]).ToImmutableArray(),map);
    }
}
