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
}
