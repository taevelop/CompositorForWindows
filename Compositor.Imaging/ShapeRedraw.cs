using Compositor.Core;

namespace Compositor.Imaging;

public static class ShapeRedraw
{
    public static Layer Apply(Layer layer, CancellationToken cancellationToken = default)
    {
        if (layer.Shape is not {} style) return layer;
        layer.Transform.Validate();
        int width = checked((int)Math.Round(layer.Transform.Width, MidpointRounding.AwayFromZero));
        int height = checked((int)Math.Round(layer.Transform.Height, MidpointRounding.AwayFromZero));
        Limits.CheckDimensions(width,height);
        if (width == layer.Pixels.Width && height == layer.Pixels.Height) return layer;
        var mask=layer.Mask;
        if(mask is {Placement:null} && (mask.Pixels.Width!=1||mask.Pixels.Height!=1))
            mask=mask with{Placement=layer.Transform};
        var pixels = ShapeRaster.Create(style,width,height,cancellationToken);
        return layer with { Pixels=pixels, Shape=style, Mask=mask };
    }
    public static Document Apply(Document document, CancellationToken cancellationToken = default)
    {
        var layers=document.Layers.ToBuilder();
        for(int i=0;i<layers.Count;i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            layers[i]=Apply(layers[i],cancellationToken);
        }
        var next=document with { Layers=layers.ToImmutable() };next.Validate();return next;
    }
}
