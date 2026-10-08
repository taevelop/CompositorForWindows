using Compositor.Core;
namespace Compositor.Imaging;
public static class GaussianBlur
{
    public static Document Preview(Document document,Guid layerId,double radius,int padding=0,CancellationToken cancellation=default)=>
        SpatialBlur.Preview(document,layerId,new(BlurKind.Gaussian,radius),padding,cancellation);
    public static Document Apply(Document document,Guid layerId,double radius,int padding=0,CancellationToken cancellation=default)=>
        SpatialBlur.Apply(document,layerId,new(BlurKind.Gaussian,radius),padding,cancellation);
}