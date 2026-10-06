using Compositor.Core;
namespace Compositor.Imaging;

/// <summary>A single reversible edit for an image layer, its selected pixels, or a visible group subtree.</summary>
public sealed class LayerTransformEdit:IDisposable
{
    private readonly EditorSession session;
    private readonly Document original;
    private readonly Layer layer;
    private readonly SelectionTransformPixels? selected;
    private readonly GroupTransform? group;
    private bool finished;
    public LayerTransform InitialTransform {get;}
    public LayerTransform Draft {get;private set;}
    public bool IsSelection=>selected is not null;
    private LayerTransformEdit(EditorSession session,Layer layer,SelectionTransformPixels? selected)
    {
        this.session=session;this.layer=layer;this.selected=selected;original=session.Document;
        group=session.SelectedLayerIds.Count>1||layer.IsGroup?new GroupTransform(original,session.SelectedLayerIds):null;
        InitialTransform=Draft=group?.Bounds??selected?.InitialTransform??layer.Transform;session.Begin();
    }
    public static LayerTransformEdit Begin(EditorSession session)
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit first.");
        if(session.ActiveLayer is not {} layer || (layer.IsAdjustment && session.SelectedLayerIds.Count<=1))
            throw new InvalidOperationException("Select an image layer or group to transform.");
        if(session.SelectedLayerIds.Count<=1&&!LayerHierarchy.Entries(session.Document).First(e=>e.Layer.Id==layer.Id).Visible)
            throw new InvalidOperationException("Show the layer and its parents before transforming.");
        SelectionTransformPixels? selection=null;
        if(session.SelectedLayerIds.Count<=1&&!layer.IsGroup&&!session.EditMask&&session.Document.Selection is {IsEmpty:false})
            selection=SelectionTransformPixels.Create(session.Document,layer.Id)??throw new InvalidOperationException("The selection does not intersect the canvas.");
        return new(session,layer,selection);
    }
    public void Preview(LayerTransform value)
    {
        if(finished)throw new ObjectDisposedException(nameof(LayerTransformEdit));
        try
        {
            value.Validate();
            var next=value==InitialTransform?original:group?.Apply(value)??selected?.Apply(value)??original.Replace(layer with{Transform=value});
            next.Validate();session.Preview(next);Draft=value;
        }
        catch{Dispose();throw;}
    }
    public void Complete()
    {
        if(finished)return;
        finished=true;session.Commit();
    }
    public void Dispose()
    {
        if(finished)return;
        finished=true;session.Cancel();
    }
}
