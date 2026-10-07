using Compositor.Core;
namespace Compositor.Imaging;

public static class LayerPlacement
{
    public static Layer Change(Layer layer,LayerTransform transform)
    {
        transform.Validate();if(transform==layer.Transform)return layer;
        var mask=layer.Mask;
        if(mask is not null)
        {
            var placement=mask.Pixels.Width==1&&mask.Pixels.Height==1 ? null :
                MaskPlacement.Move(mask.Placement,mask.Linked,layer.Transform,transform);
            if(placement!=mask.Placement)mask=mask with{Placement=placement};
        }
        return layer with{Transform=transform,Mask=mask};
    }
}
