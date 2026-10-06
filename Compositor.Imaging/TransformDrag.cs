using Compositor.Core;
namespace Compositor.Imaging;
public enum TransformDragMode { Move, Resize, Rotate }
public sealed record TransformDrag(LayerTransform Original,PointD Start,TransformDragMode Mode,int Handle=0)
{
    public static PointD Point(LayerTransform t,PointD unit)
    {
        double r=t.Rotation%360*Math.PI/180,c=Math.Cos(r),s=Math.Sin(r);
        double x=(unit.X-.5)*t.Width,y=(unit.Y-.5)*t.Height;
        return new(t.X+t.Width/2+x*c-y*s,t.Y+t.Height/2+x*s+y*c);
    }
    public static bool Contains(LayerTransform t,PointD point)
    {
        var p=(t with{FlipX=false,FlipY=false}).ToPixels(point,1,1);
        return p.X>=0&&p.X<=1&&p.Y>=0&&p.Y<=1;
    }
    public LayerTransform Update(PointD point,bool lockRatio=false,bool shift=false,bool fromCenter=false)
    {
        if(!double.IsFinite(point.X)||!double.IsFinite(point.Y))throw new ArgumentException("Invalid transform pointer.");
        Original.Validate();if(point==Start)return Original;var result=Original;
        if(Mode==TransformDragMode.Move)
        {
            double dx=point.X-Start.X,dy=point.Y-Start.Y;
            if(shift){if(Math.Abs(dx)>=Math.Abs(dy))dy=0;else dx=0;}
            result=Original with{X=Original.X+dx,Y=Original.Y+dy};
        }
        else if(Mode==TransformDragMode.Rotate)
        {
            double cx=Original.X+Original.Width/2,cy=Original.Y+Original.Height/2;
            double angle=Original.Rotation+(Math.Atan2(point.Y-cy,point.X-cx)-Math.Atan2(Start.Y-cy,Start.X-cx))*180/Math.PI;
            if(shift)angle=Math.Round(angle/15,MidpointRounding.AwayFromZero)*15;
            result=Original with{Rotation=angle};
        }
        else if(Mode==TransformDragMode.Resize)
        {
            if(Handle<0||Handle>=CropGeometry.Handles.Count)throw new ArgumentOutOfRangeException(nameof(Handle));
            var handle=CropGeometry.Handles[Handle];var anchorUnit=fromCenter?new PointD(.5,.5):new(1-handle.X,1-handle.Y);
            var anchor=Point(Original,anchorUnit);var initial=Point(Original,handle);
            double dx=initial.X+point.X-Start.X-anchor.X,dy=initial.Y+point.Y-Start.Y-anchor.Y;
            double angle=Original.Rotation%360*Math.PI/180,c=Math.Cos(angle),s=Math.Sin(angle),span=fromCenter?2:1;
            double lx=(dx*c+dy*s)*span,ly=(-dx*s+dy*c)*span,sx=handle.X*2-1,sy=handle.Y*2-1;
            double rawW=sx==0?Original.Width:lx*sx,rawH=sy==0?Original.Height:ly*sy;
            double w=Math.Max(1,Math.Abs(rawW)),h=Math.Max(1,Math.Abs(rawH));
            if(lockRatio!=shift)
            {
                double factor=sx==0?h/Original.Height:sy==0?w/Original.Width:
                    Math.Max(1/Math.Min(Original.Width,Original.Height),(lx*sx*Original.Width+ly*sy*Original.Height)/(Original.Width*Original.Width+Original.Height*Original.Height));
                w=Original.Width*factor;h=Original.Height*factor;
            }
            double ox=(.5-anchorUnit.X)*w*(rawW<0?-1:1),oy=(.5-anchorUnit.Y)*h*(rawH<0?-1:1);
            result=Original with{Width=w,Height=h,X=anchor.X+ox*c-oy*s-w/2,Y=anchor.Y+ox*s+oy*c-h/2,
                FlipX=Original.FlipX^(rawW<0),FlipY=Original.FlipY^(rawH<0)};
        }
        else throw new ArgumentOutOfRangeException(nameof(Mode));
        try{result.Validate();return result;}catch(InvalidDataException){return Original;}
    }
}
