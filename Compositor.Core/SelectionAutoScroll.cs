namespace Compositor.Core;
public static class SelectionAutoScroll
{
    /// <summary>Device-independent view pixels per 60 Hz tick, matching the Mac marquee rule.</summary>
    public static PointD Delta(PointD point,double width,double height)
    {
        if(!double.IsFinite(point.X)||!double.IsFinite(point.Y)||!double.IsFinite(width)||!double.IsFinite(height)||width<=0||height<=0)return new(0,0);
        static double Speed(double past)=>past<=0?0:Math.Min(40,2+past*.4);
        return new(Speed(12-point.X)-Speed(point.X-(width-12)),
            Speed(12-point.Y)-Speed(point.Y-(height-12)));
    }
}
