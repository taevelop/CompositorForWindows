using Compositor.Core;
namespace Compositor.Imaging;
public enum GradientShape { Linear,Radial }
public enum GradientStyle { ForegroundToBackground,ForegroundToTransparent }
public sealed record GradientFillSettings(PointD Start,PointD End,byte ForegroundRed,byte ForegroundGreen,byte ForegroundBlue,
    byte BackgroundRed,byte BackgroundGreen,byte BackgroundBlue,GradientShape Shape=GradientShape.Linear,
    GradientStyle Style=GradientStyle.ForegroundToTransparent,bool Reversed=false,double Opacity=1)
{
    public void Validate()
    {
        if(!Enum.IsDefined(Shape)||!Enum.IsDefined(Style)||!double.IsFinite(Opacity)||Opacity<0||Opacity>1||
            !double.IsFinite(Start.X)||!double.IsFinite(Start.Y)||!double.IsFinite(End.X)||!double.IsFinite(End.Y)||
            Math.Abs(Start.X)>1_000_000||Math.Abs(Start.Y)>1_000_000||Math.Abs(End.X)>1_000_000||Math.Abs(End.Y)>1_000_000)
            throw new ArgumentException("Invalid gradient settings.");
    }
    internal double LengthSquared=>(End.X-Start.X)*(End.X-Start.X)+(End.Y-Start.Y)*(End.Y-Start.Y);
    internal (double Red,double Green,double Blue,double Alpha) Sample(PointD point)
    {
        double dx=End.X-Start.X,dy=End.Y-Start.Y,x=point.X-Start.X,y=point.Y-Start.Y;
        double t=Math.Clamp(Shape==GradientShape.Linear?(x*dx+y*dy)/LengthSquared:Math.Sqrt((x*x+y*y)/LengthSquared),0,1);
        if(Reversed)t=1-t;
        if(Style==GradientStyle.ForegroundToTransparent)return(ForegroundRed,ForegroundGreen,ForegroundBlue,(1-t)*Opacity);
        return(ForegroundRed+(BackgroundRed-ForegroundRed)*t,ForegroundGreen+(BackgroundGreen-ForegroundGreen)*t,
            ForegroundBlue+(BackgroundBlue-ForegroundBlue)*t,Opacity);
    }
}
public static class GradientFill
{
    public static Document Apply(Document document,Guid layerId,GradientFillSettings settings,bool editMask=false,CancellationToken cancellation=default)
    {
        settings.Validate();cancellation.ThrowIfCancellationRequested();
        if(settings.LengthSquared<.25||settings.Opacity==0)return document;
        return LayerFill.ApplyWithUndoBudget(document,layerId,0,0,0,editMask,cancellation,EditorSession.MaxHistoryBytes,settings);
    }
}
