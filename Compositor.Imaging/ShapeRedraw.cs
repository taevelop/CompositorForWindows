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
        // The source mask grid must remain intact. Independent mask placement is a separate
        // document feature; refuse this case rather than resampling and changing coverage.
        if (layer.Mask is {} mask && (mask.Pixels.Width != 1 || mask.Pixels.Height != 1))
            throw new NotSupportedException("Resizing a shape with a pixel mask requires independent mask placement. Remove the mask or cancel the resize.");
        var pixels = ShapeRaster.Create(style,width,height,cancellationToken);
        return layer with { Pixels=pixels, Shape=style };
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
