using Compositor.Core;
namespace Compositor.Imaging;

public enum CropDragMode { Create, Move, Resize }
public sealed record CropDrag(CropFrame Original, PointD Start, CropDragMode Mode, int Handle = 0)
{
    public CropFrame Update(PointD point, double? ratio, bool symmetric) => Mode switch
    {
        CropDragMode.Create => CropGeometry.Create(Start, point, ratio, symmetric),
        CropDragMode.Move => CropGeometry.Move(Original, Start, point),
        _ => CropGeometry.Resize(Original, Handle, Start, point, ratio is not null, symmetric)
    };
}
public static class CropSnapping
{
    public static CropFrame Apply(CropFrame frame, CropDrag drag, PointD point, double? ratio,
        bool symmetric, IReadOnlyList<double> xs, IReadOnlyList<double> ys, double tolerance)
    {
        if (!double.IsFinite(tolerance) || tolerance <= 0) return frame;
        double? Nearest(double edge, IReadOnlyList<double> targets)
        {
            double? best = null;
            foreach (double target in targets)
                if (double.IsFinite(target) && Math.Abs(target-edge) <= tolerance &&
                    (best is null || Math.Abs(target-edge) < Math.Abs(best.Value-edge))) best = target;
            return best;
        }
        double Shift(double a, double b, IReadOnlyList<double> targets)
        {
            double? da = Nearest(a, targets)-a, db = Nearest(b, targets)-b;
            return da is null ? db ?? 0 : db is null || Math.Abs(da.Value) <= Math.Abs(db.Value) ? da.Value : db.Value;
        }
        if (drag.Mode == CropDragMode.Move)
            return CropGeometry.Snap(frame.X+Shift(frame.X,frame.Right,xs),frame.Y+Shift(frame.Y,frame.Bottom,ys),frame.Width,frame.Height);
        if (ratio is not null) return frame;
        var handle = drag.Mode == CropDragMode.Resize ? CropGeometry.Handles[drag.Handle] : new PointD(0,0);
        bool horizontal=handle.X!=.5, vertical=handle.Y!=.5;
        double left=frame.X, top=frame.Y, right=frame.Right, bottom=frame.Bottom;
        if (horizontal)
        {
            if (Math.Abs(point.X-left)<=Math.Abs(point.X-right)) { if(Nearest(left,xs) is {} x && x<right)left=x; }
            else if(Nearest(right,xs) is {} x && x>left)right=x;
        }
        if (vertical)
        {
            if (Math.Abs(point.Y-top)<=Math.Abs(point.Y-bottom)) { if(Nearest(top,ys) is {} y && y<bottom)top=y; }
            else if(Nearest(bottom,ys) is {} y && y>top)bottom=y;
        }
        if (symmetric)
        {
            double cx=drag.Mode==CropDragMode.Create?drag.Start.X:drag.Original.X+drag.Original.Width/2d;
            double cy=drag.Mode==CropDragMode.Create?drag.Start.Y:drag.Original.Y+drag.Original.Height/2d;
            double halfX=point.X>=cx?right-cx:cx-left, halfY=point.Y>=cy?bottom-cy:cy-top;
            if(horizontal&&halfX>=.5){left=cx-halfX;right=cx+halfX;}
            if(vertical&&halfY>=.5){top=cy-halfY;bottom=cy+halfY;}
        }
        return CropGeometry.Snap(left,top,right-left,bottom-top);
    }
}
