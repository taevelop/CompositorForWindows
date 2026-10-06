using System.Collections.Immutable;
using Compositor.Core;
namespace Compositor.Imaging;

public readonly record struct CropFrame(int X,int Y,int Width,int Height)
{
    public double Right=>(double)X+Width;
    public double Bottom=>(double)Y+Height;
    public void Validate()
    {
        Limits.CheckDimensions(Width,Height);
        if(Math.Abs((long)X)>1_000_000||Math.Abs((long)Y)>1_000_000)throw new InvalidDataException("Invalid crop origin.");
    }
}
public static class CropGeometry
{
    public static IReadOnlyList<PointD> Handles { get; }=Array.AsReadOnly<PointD>([new(0,0),new(.5,0),new(1,0),new(1,.5),new(1,1),new(.5,1),new(0,1),new(0,.5)]);
    private static double Round(double n)=>Math.Round(n,MidpointRounding.AwayFromZero);
    public static CropFrame Snap(double x,double y,double width,double height)
    {
        if(new[]{x,y,width,height}.Any(v=>!double.IsFinite(v)))throw new ArgumentException("Invalid crop coordinates.");
        double left=Round(Math.Min(x,x+width)),top=Round(Math.Min(y,y+height));
        double w=Math.Max(1,Round(Math.Max(x,x+width))-left),h=Math.Max(1,Round(Math.Max(y,y+height))-top);
        if(Math.Abs(left)>1_000_000||Math.Abs(top)>1_000_000||w>30000||h>30000)throw new InvalidDataException("Crop is outside supported bounds.");
        var frame=new CropFrame((int)left,(int)top,(int)w,(int)h);frame.Validate();return frame;
    }
    public static CropFrame Create(PointD start,PointD end,double? ratio=null,bool symmetric=false)
    {
        if(ratio is { } r&&(!double.IsFinite(r)||r<=0))throw new ArgumentOutOfRangeException(nameof(ratio));
        double dx=end.X-start.X,dy=end.Y-start.Y;
        if(ratio is { } aspect)
        {if(Math.Abs(dx)>Math.Abs(dy)*aspect)dy=Math.CopySign(Math.Abs(dx)/aspect,dy);else dx=Math.CopySign(Math.Abs(dy)*aspect,dx);}
        return symmetric?Snap(start.X-Math.Abs(dx),start.Y-Math.Abs(dy),Math.Abs(dx)*2,Math.Abs(dy)*2):
            Snap(start.X,start.Y,dx,dy);
    }
    public static CropFrame Move(CropFrame frame,PointD start,PointD end)
    {frame.Validate();return Snap(frame.X+end.X-start.X,frame.Y+end.Y-start.Y,frame.Width,frame.Height);}
    public static CropFrame Resize(CropFrame frame,int handle,PointD start,PointD point,bool lockRatio=false,bool symmetric=false)
    {
        frame.Validate();if(handle<0||handle>=Handles.Count)throw new ArgumentOutOfRangeException(nameof(handle));
        var h=Handles[handle];var anchor=symmetric?new PointD(.5,.5):new PointD(1-h.X,1-h.Y);
        double ax=frame.X+anchor.X*frame.Width,ay=frame.Y+anchor.Y*frame.Height;
        double localX=(frame.X+h.X*frame.Width+point.X-start.X-ax)*(symmetric?2:1);
        double localY=(frame.Y+h.Y*frame.Height+point.Y-start.Y-ay)*(symmetric?2:1);
        double sx=h.X*2-1,sy=h.Y*2-1;
        double rawW=sx==0?frame.Width:localX*sx,rawH=sy==0?frame.Height:localY*sy;
        double width=Math.Max(1,Math.Abs(rawW)),height=Math.Max(1,Math.Abs(rawH));
        if(lockRatio)
        {
            double factor=sx==0?height/frame.Height:sy==0?width/frame.Width:
                Math.Max(1d/Math.Min(frame.Width,frame.Height),(localX*sx*frame.Width+localY*sy*frame.Height)/((double)frame.Width*frame.Width+(double)frame.Height*frame.Height));
            width=frame.Width*factor;height=frame.Height*factor;
        }
        double cx=ax+(.5-anchor.X)*width*(rawW<0?-1:1),cy=ay+(.5-anchor.Y)*height*(rawH<0?-1:1);
        return Snap(cx-width/2,cy-height/2,width,height);
    }
    public static CropFrame WithRatio(CropFrame frame,double ratio)
    {
        frame.Validate();if(!double.IsFinite(ratio)||ratio<=0)throw new ArgumentOutOfRangeException(nameof(ratio));
        double height=frame.Width/ratio;
        return Snap(frame.X,frame.Y+(frame.Height-height)/2,frame.Width,height);
    }
}
public static class CanvasCrop
{
    /// <summary>Crop changes the canvas, retaining all off-canvas layer pixels and linked masks.</summary>
    public static Document Apply(Document document,CropFrame frame)
    {
        frame.Validate();
        if(frame.X==0&&frame.Y==0&&frame.Width==document.Width&&frame.Height==document.Height&&document.Selection is null)return document;
        var layers=document.Layers.Select(layer=>frame.X==0&&frame.Y==0?layer:
            layer with{Transform=layer.Transform with{X=layer.Transform.X-frame.X,Y=layer.Transform.Y-frame.Y}}).ToImmutableArray();
        var next=document with{Width=frame.Width,Height=frame.Height,Layers=layers,Selection=null};
        next.Validate();return next;
    }
}
