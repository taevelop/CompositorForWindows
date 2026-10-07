namespace Compositor.Core;

public enum ShapeKind { Rectangle,Ellipse,Line }

/// <summary>Original .comp shape values; coordinates are fractions of the layer box.</summary>
public sealed record LayerShapeStyle(ShapeKind Kind,double Red,double Green,double Blue,double CornerRadius=0,
    double? LineWidth=null,PointD? Start=null,PointD? End=null)
{
    public void Validate()
    {
        if(!Enum.IsDefined(Kind)||new[]{Red,Green,Blue}.Any(value=>!double.IsFinite(value)||value<0||value>1)||
            !double.IsFinite(CornerRadius)||CornerRadius<0||CornerRadius>300_000||
            (LineWidth is {} width&&(!double.IsFinite(width)||width<0||width>300_000)))
            throw new InvalidDataException("Invalid shape style.");
        foreach(var point in new[]{Start,End})
            if(point is {} p&&(!double.IsFinite(p.X)||!double.IsFinite(p.Y)||p.X<0||p.X>1||p.Y<0||p.Y>1))
                throw new InvalidDataException("Invalid shape endpoint.");
    }
}
