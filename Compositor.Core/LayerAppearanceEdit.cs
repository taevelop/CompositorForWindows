using System.Collections.Immutable;
namespace Compositor.Core;
public static class LayerAppearanceEdit
{
    public static Document Apply(Document source,IEnumerable<Guid> selection,double? opacity=null,bool? visible=null,BlendMode? blend=null)
    {
        var ids=selection.ToHashSet();
        if(ids.Any(id=>!source.Layers.Any(l=>l.Id==id)))throw new InvalidOperationException("Layer no longer exists.");
        if(opacity is {} value&&(!double.IsFinite(value)||value<0||value>1))throw new InvalidDataException("Opacity must be between 0 and 100 percent.");
        if(blend is {} mode&&!Enum.IsDefined(mode))throw new InvalidDataException("Invalid blend mode.");
        if(blend is not null&&source.Layers.Any(l=>ids.Contains(l.Id)&&l.IsGroup))throw new InvalidOperationException("Folder blending is not supported. Select image or adjustment layers to change blending.");
        if(ids.Count==0||(opacity is null&&visible is null&&blend is null))return source;
        var layers=source.Layers.Select(l=>ids.Contains(l.Id)?l with{Opacity=opacity??l.Opacity,Visible=visible??l.Visible,Blend=blend??l.Blend}:l).ToImmutableArray();
        if(layers.SequenceEqual(source.Layers))return source;
        var next=source with{Layers=layers};next.Validate();return next;
    }
}