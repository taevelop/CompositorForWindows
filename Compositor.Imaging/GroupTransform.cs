using System.Collections.Immutable;
using Compositor.Core;
namespace Compositor.Imaging;

/// <summary>Original-style folder transform: only effectively visible image descendants participate.</summary>
public sealed class GroupTransform
{
    private readonly Document original;
    private readonly HashSet<Guid> members;
    public LayerTransform Bounds {get;}
    public int MemberCount=>members.Count;
    public GroupTransform(Document document,Guid root):this(document,new[]{root}) {}
    public GroupTransform(Document document,IEnumerable<Guid> roots)
    {
        original=document;var subtree=roots.SelectMany(root=>LayerHierarchy.Subtree(document,root)).ToHashSet();
        members=LayerHierarchy.Entries(document).Where(e=>subtree.Contains(e.Layer.Id)&&e.Visible&&!e.Layer.IsGroup&&!e.Layer.IsAdjustment).Select(e=>e.Layer.Id).ToHashSet();
        if(members.Count==0)throw new InvalidOperationException("The group has no visible image layers to transform.");
        var points=document.Layers.Where(l=>members.Contains(l.Id)).SelectMany(l=>new[]{new PointD(0,0),new PointD(1,0),new PointD(1,1),new PointD(0,1)}.Select(p=>l.Transform.ToDocument(p,1,1))).ToArray();
        double x=points.Min(p=>p.X),y=points.Min(p=>p.Y);
        Bounds=new(x,y,Math.Max(1,points.Max(p=>p.X)-x),Math.Max(1,points.Max(p=>p.Y)-y));Bounds.Validate();
    }
    public static LayerTransform Following(LayerTransform source,LayerTransform from,LayerTransform to)
    {
        from.Validate();to.Validate();source.Validate();
        if(from==to)return source;
        if(from.Width==to.Width&&from.Height==to.Height&&from.Rotation==to.Rotation&&from.FlipX==to.FlipX&&from.FlipY==to.FlipY)
            return source with{X=source.X+to.X-from.X,Y=source.Y+to.Y-from.Y};
        PointD Map(PointD p)=>to.ToDocument(from.ToPixels(source.ToDocument(p,1,1),1,1),1,1);
        var o=Map(new(0,0));var px=Map(new(1,0));var py=Map(new(0,1));var center=Map(new(.5,.5));
        double a=px.X-o.X,b=px.Y-o.Y,c=py.X-o.X,d=py.Y-o.Y,sign=source.FlipX?-1:1;
        double angle=Math.Atan2(b*sign,a*sign),along=-c*Math.Sin(angle)+d*Math.Cos(angle);
        double w=double.Hypot(a,b),h=Math.Abs(along),degrees=angle*180/Math.PI;
        return source with{X=center.X-w/2,Y=center.Y-h/2,Width=w,Height=h,FlipY=along<0,
            Rotation=degrees+Math.Round((source.Rotation-degrees)/360,MidpointRounding.AwayFromZero)*360};
    }
    public Document Apply(LayerTransform placement)
    {
        placement.Validate();if(placement==Bounds)return original;
        var layers=original.Layers.Select(l=>members.Contains(l.Id)?LayerPlacement.Change(l,Following(l.Transform,Bounds,placement)):l).ToImmutableArray();
        var next=original with{Layers=layers};next.Validate();return next;
    }
}
