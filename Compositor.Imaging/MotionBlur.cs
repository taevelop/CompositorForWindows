using Compositor.Core;
namespace Compositor.Imaging;
public static class MotionBlur
{
    public static Document Preview(Document document,Guid layerId,double distance,double angle,int padding=0,CancellationToken cancellation=default)=>
        SpatialBlur.Preview(document,layerId,new(BlurKind.Motion,distance,angle),padding,cancellation);
    public static Document Apply(Document document,Guid layerId,double distance,double angle,int padding=0,CancellationToken cancellation=default)=>
        SpatialBlur.Apply(document,layerId,new(BlurKind.Motion,distance,angle),padding,cancellation);
}
