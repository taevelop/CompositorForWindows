using Compositor.Core;
namespace Compositor.Imaging;

/// <summary>Display-only rounded corners; never publish this document to an editor session.</summary>
public sealed class ShapeTransformPreview
{
    private Document? source, baseline, result;
    public void Clear() { source=null;baseline=null;result=null; }
    public Document Create(Document document, Document original)
    {
        if(ReferenceEquals(source,document)&&ReferenceEquals(baseline,original))return result!;
        var next=document;
        foreach(var layer in document.Layers)
        {
            var before=original.Layers.FirstOrDefault(l=>l.Id==layer.Id);
            if(before is null||before.Transform==layer.Transform||!ReferenceEquals(before.Pixels,layer.Pixels)||
                layer.Shape is not {Kind:ShapeKind.Rectangle,CornerRadius:>0} style)continue;
            var t=layer.Transform;
            if(Math.Abs(t.Width-layer.Pixels.Width)<.5&&Math.Abs(t.Height-layer.Pixels.Height)<.5)continue;
            double factor=Math.Min(1,2048/Math.Max(t.Width,t.Height));
            int width=Math.Max(1,(int)Math.Round(t.Width*factor,MidpointRounding.AwayFromZero));
            int height=Math.Max(1,(int)Math.Round(t.Height*factor,MidpointRounding.AwayFromZero));
            var pixels=ShapeRaster.Create(style with{CornerRadius=style.CornerRadius*factor},width,height);
            var mask=layer.Mask;
            if(mask is not null&&mask.Placement is null&&(mask.Pixels.Width!=1||mask.Pixels.Height!=1))
                mask=mask with{Placement=t};
            next=next.Replace(layer with{Pixels=pixels,Shape=style,Mask=mask});
        }
        source=document;baseline=original;result=next;return next;
    }
}
