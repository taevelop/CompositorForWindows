using Compositor.Core;
namespace Compositor.Imaging;

public static class LayerMaskCopy
{
    public static Document Apply(Document document,Guid sourceId,Guid targetId)
    {
        document.Validate();if(sourceId==targetId)return document;
        var source=document.Layers.First(l=>l.Id==sourceId);var target=document.Layers.First(l=>l.Id==targetId);
        if(source.Mask is not {} mask)throw new InvalidOperationException("The source layer has no mask.");
        if(target.IsGroup)throw new InvalidOperationException("Masks cannot be copied onto a group.");
        // Original Option-drag pins the copy to its existing document position.
        var copied=mask with{Placement=mask.Placement??source.Transform};
        var result=document.Replace(target with{Mask=copied});result.Validate();
        if(EditorSession.UndoBytesRequired(document,result)>EditorSession.MaxHistoryBytes)throw new InvalidOperationException("Replacing this mask exceeds the Undo memory limit.");
        return result;
    }
}
