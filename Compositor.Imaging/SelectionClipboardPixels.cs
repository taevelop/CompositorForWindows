using Compositor.Core;
using SkiaSharp;
namespace Compositor.Imaging;

public sealed record PixelClipboardContent(Raster Pixels,PointD Origin);
public static class SelectionClipboardPixels
{
    public static PixelClipboardContent? Copy(Document document,Guid? activeLayer,bool mask=false,bool merged=false)
    {
        if(document.Selection is {IsEmpty:true})return null;
        int x=0,y=0,right=document.Width,bottom=document.Height;
        SelectionCoverage? coverage=null;
        if(document.Selection is { } selection)
        {
            using var path=SelectionGeometry.Path(selection);var b=path.Bounds;
            double margin=Math.Ceiling(selection.Feather*2);
            x=(int)Math.Clamp(Math.Floor(b.Left-margin+.001),0,document.Width);
            y=(int)Math.Clamp(Math.Floor(b.Top-margin+.001),0,document.Height);
            right=(int)Math.Clamp(Math.Ceiling(b.Right+margin-.001),0,document.Width);
            bottom=(int)Math.Clamp(Math.Ceiling(b.Bottom+margin-.001),0,document.Height);
            if(right<=x||bottom<=y)return null;
            coverage=SelectionCoverage.Create(selection,document.Width,document.Height);
            if(coverage.IsEmpty)return null;
        }
        var draw=document;
        if(!merged)
        {
            var source=document.Layers.FirstOrDefault(l=>l.Id==activeLayer)??throw new InvalidOperationException("Select a layer to copy.");
            if(mask)
            {
                if(source.Mask is not { } owned)throw new InvalidOperationException("Select a layer mask to copy.");
                var raw=Layer.Blank(source.Name,source.Pixels.Width,source.Pixels.Height) with{Pixels=owned.EditingPixels(source.Pixels.Width,source.Pixels.Height),Transform=source.Transform};
                draw=document with{Layers=[raw],Selection=null};
            }
            else
            {
                if(source.IsGroup||source.IsAdjustment)throw new InvalidOperationException("Select an image layer or use Copy Merged.");
                var raw=Layer.Blank(source.Name,source.Pixels.Width,source.Pixels.Height) with{Pixels=source.Pixels,Transform=source.Transform};
                draw=document with{Layers=[raw],Selection=null};
            }
        }
        using var bitmap=new SKBitmap(CanvasRenderer.Info(right-x,bottom-y));
        using(var canvas=new SKCanvas(bitmap))
        {
            canvas.Clear(mask&&!merged?SKColors.Black:SKColors.Transparent);canvas.Translate(-x,-y);
            using var renderer=new CanvasRenderer();renderer.Draw(canvas,draw);canvas.Flush();
        }
        var pixels=Raster.FromRgba(bitmap.Width,bitmap.Height,bitmap.GetPixelSpan());
        if(coverage is not null)pixels=SelectionPixels.Blend(new(bitmap.Width,bitmap.Height),pixels,new(x,y,bitmap.Width,bitmap.Height),coverage);
        return new(pixels,new(x,y));
    }

    /// <summary>Publish first; never delete source pixels if the system clipboard write fails.</summary>
    public static bool Cut(EditorSession session,Action<PixelClipboardContent> publish)
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit first.");
        if(session.Document.Selection is null)return false;
        var copied=Copy(session.Document,session.ActiveLayerId,session.EditMask);
        if(copied is null)return false;
        var original=session.Document;var layer=session.ActiveLayer!;
        if(!LayerHierarchy.Entries(original).First(e=>e.Layer.Id==layer.Id).Visible)
            throw new InvalidOperationException("Show the layer before cutting pixels.");
        var coverage=SelectionCoverage.Create(original.Selection!,original.Width,original.Height);
        Layer updated;
        if(session.EditMask)
        {
            if(layer.Mask is not {Enabled:true} mask)throw new InvalidOperationException("Enable the mask before cutting pixels.");
            var source=mask.EditingPixels(layer.Pixels.Width,layer.Pixels.Height);
            updated=layer with{Mask=mask.WithPixels(SelectionPixels.Blend(source,LayerMask.Solid(source.Width,source.Height,0).Pixels,layer.Transform,coverage))};
        }
        else updated=layer with{Pixels=SelectionPixels.Blend(layer.Pixels,new(layer.Pixels.Width,layer.Pixels.Height),layer.Transform,coverage)};
        var next=original.Replace(updated);next.Validate();
        if(EditorSession.UndoBytesRequired(original,next)>EditorSession.MaxHistoryBytes)
            throw new InvalidOperationException("This cut exceeds the 256 MiB Undo limit.");
        session.Apply(_=>{publish(copied);return next;});return true;
    }

    public static Guid Paste(EditorSession session,PixelClipboardContent content,bool preserveOrigin=true,string name="Pasted pixels")
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit first.");
        var doc=session.Document;
        var origin=preserveOrigin?content.Origin:new(Math.Floor((doc.Width-content.Pixels.Width)/2d),Math.Floor((doc.Height-content.Pixels.Height)/2d));
        var active=session.ActiveLayer;Guid? parent=active?.IsGroup==true?active.Id:active?.ParentId;
        var layer=new Layer(Guid.NewGuid(),name,content.Pixels,new(origin.X,origin.Y,content.Pixels.Width,content.Pixels.Height),ParentId:parent);
        int index=active is null?doc.Layers.Length:doc.Layers.IndexOf(active)+1;
        var next=doc with{Layers=doc.Layers.Insert(index,layer),Selection=null};next.Validate();
        session.Apply(_=>{session.ActiveLayerId=layer.Id;session.EditMask=false;return next;});
        return layer.Id;
    }
}
