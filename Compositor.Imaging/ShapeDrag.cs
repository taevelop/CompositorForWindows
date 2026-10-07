using Compositor.Core;
namespace Compositor.Imaging;

public sealed record ShapeDraft(LayerShapeStyle Style,LayerTransform Transform);
public sealed class ShapeDrag
{
    public PointD Anchor {get;}
    public LayerShapeStyle Style {get;}
    public ShapeDrag(PointD anchor,LayerShapeStyle style)
    {
        CheckPoint(anchor);style.Validate();
        Anchor=new(Round(anchor.X),Round(anchor.Y));Style=style;
    }
    private static double Round(double value)=>Math.Round(value,MidpointRounding.AwayFromZero);
    private static void CheckPoint(PointD point)
    {
        if(!double.IsFinite(point.X)||!double.IsFinite(point.Y)||Math.Abs(point.X)>1_000_000||Math.Abs(point.Y)>1_000_000)
            throw new InvalidDataException("Invalid shape pointer.");
    }
    public ShapeDraft? Update(PointD point,bool shift=false,bool centered=false)
    {
        CheckPoint(point);double dx=point.X-Anchor.X,dy=point.Y-Anchor.Y;
        if(Style.Kind==ShapeKind.Line)
        {
            if(shift){double angle=Round(Math.Atan2(dy,dx)/(Math.PI/4))*Math.PI/4,length=double.Hypot(dx,dy);dx=Math.Cos(angle)*length;dy=Math.Sin(angle)*length;}
            double thickness=Math.Max(1,Style.LineWidth??1);
            double x=Math.Min(Anchor.X,Anchor.X+dx)-thickness/2,y=Math.Min(Anchor.Y,Anchor.Y+dy)-thickness/2;
            double width=Math.Abs(dx)+thickness,height=Math.Abs(dy)+thickness;
            var style=Style with{Start=new((Anchor.X-x)/width,(Anchor.Y-y)/height),End=new((Anchor.X+dx-x)/width,(Anchor.Y+dy-y)/height)};
            return new(style,new(x,y,width,height));
        }
        dx=Round(point.X)-Anchor.X;dy=Round(point.Y)-Anchor.Y;
        if(shift){double side=Math.Max(Math.Abs(dx),Math.Abs(dy));dx=dx<0?-side:side;dy=dy<0?-side:side;}
        double w=Math.Abs(dx)*(centered?2:1),h=Math.Abs(dy)*(centered?2:1);
        if(w<1||h<1)return null;
        return new(Style with{Start=null,End=null,LineWidth=null},new(centered?Anchor.X-Math.Abs(dx):Math.Min(Anchor.X,Anchor.X+dx),
            centered?Anchor.Y-Math.Abs(dy):Math.Min(Anchor.Y,Anchor.Y+dy),w,h));
    }
}
public static class ShapeInsert
{
    public sealed record Prepared(Document Document,Guid LayerId);
    public static Guid Add(EditorSession session,ShapeDraft draft,CancellationToken cancellationToken=default)
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the current edit first.");
        var prepared=Prepare(session.Document,session.ActiveLayerId,draft,cancellationToken);
        session.Apply(_=>{session.ActiveLayerId=prepared.LayerId;session.EditMask=false;return prepared.Document;});return prepared.LayerId;
    }
    public static Prepared Prepare(Document document,Guid? activeLayerId,ShapeDraft draft,CancellationToken cancellationToken=default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        draft.Transform.Validate();draft.Style.Validate();document.Validate();
        var active=activeLayerId is {} id ? document.Layers.FirstOrDefault(l=>l.Id==id)??throw new InvalidOperationException("Active layer no longer exists.") : null;
        if(document.Layers.Length>=10000)throw new InvalidDataException("Too many layers.");
        int width=(int)draft.Transform.Width,height=(int)draft.Transform.Height;Limits.CheckDimensions(width,height);
        long existing=document.Layers.Where(l=>!l.IsAdjustment&&(l.Pixels.Tiles.Count!=0||l.Mask is not null)).Sum(l=>(long)l.Pixels.Width*l.Pixels.Height);
        if((long)width*height>Limits.MaxPixels-existing)throw new InvalidDataException("Shape exceeds the project image budget.");
        var pixels=ShapeRaster.Create(draft.Style,draft.Transform.Width,draft.Transform.Height,cancellationToken);
        string name;int number=1;
        do{name=$"{draft.Style.Kind} {number++}";}while(document.Layers.Any(l=>l.Name==name));
        var parent=active?.IsGroup==true?active.Id:active?.ParentId;
        var layer=new Layer(Guid.NewGuid(),name,pixels,draft.Transform,ParentId:parent){Shape=draft.Style};
        var next=document with{Layers=document.Layers.Insert(active is null?document.Layers.Length:document.Layers.IndexOf(active)+1,layer)};
        next.Validate();cancellationToken.ThrowIfCancellationRequested();
        return new(next,layer.Id);
    }
}
