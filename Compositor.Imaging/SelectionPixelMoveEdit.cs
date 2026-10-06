using Compositor.Core;
namespace Compositor.Imaging;

/// <summary>One transaction for moving or duplicating selected image pixels and their outline.</summary>
public sealed class SelectionPixelMoveEdit : IDisposable
{
    private readonly EditorSession session;
    private readonly Document original;
    private readonly Layer source;
    private readonly SelectedPixelTransfer transfer;
    private readonly bool duplicate;
    private PointD offset;
    private bool finished;
    private SelectionPixelMoveEdit(EditorSession session,Layer source,SelectedPixelTransfer transfer,bool duplicate)
    {this.session=session;original=session.Document;this.source=source;this.transfer=transfer;this.duplicate=duplicate;session.Begin();}
    public static SelectionPixelMoveEdit? Begin(EditorSession session,bool duplicate=false)
    {
        if(session.InTransaction)throw new InvalidOperationException("Finish the active edit before moving pixels.");
        if(session.EditMask||session.ActiveLayer is not {IsGroup:false,IsAdjustment:false} layer)
            throw new InvalidOperationException("Select an image layer and Edit image to move pixels.");
        if(!LayerHierarchy.Entries(session.Document).First(e=>e.Layer.Id==layer.Id).Visible)
            throw new InvalidOperationException("Show the layer and its parent groups before moving pixels.");
        if(session.Document.Selection is not {IsEmpty:false} selected)return null;
        var coverage=SelectionCoverage.Create(selected,session.Document.Width,session.Document.Height);
        var transfer=SelectedPixelTransfer.Lift(layer,coverage);
        return transfer is null?null:new(session,layer,transfer,duplicate);
    }
    public void Preview(double documentDx,double documentDy)
    {
        if(finished)throw new ObjectDisposedException(nameof(SelectionPixelMoveEdit));
        try
        {
            if(!double.IsFinite(documentDx)||!double.IsFinite(documentDy)||Math.Abs(documentDx)>1_000_000||Math.Abs(documentDy)>1_000_000)
                throw new ArgumentOutOfRangeException(nameof(documentDx));
            var nextOffset=new PointD(Math.Round(documentDx,MidpointRounding.AwayFromZero),Math.Round(documentDy,MidpointRounding.AwayFromZero));
            if(nextOffset==offset)return;
            if(nextOffset==new PointD(0,0)){session.Preview(original);offset=nextOffset;return;}
            int width=source.Pixels.Width,height=source.Pixels.Height;
            var zero=source.Transform.ToPixels(new(0,0),width,height);
            var move=source.Transform.ToPixels(nextOffset,width,height);
            double dx=move.X-zero.X,dy=move.Y-zero.Y;
            PointD[] corners=[new(0,0),new(original.Width,0),new(original.Width,original.Height),new(0,original.Height)];
            var local=corners.Select(p=>source.Transform.ToPixels(p,width,height)).ToArray();
            double allowedLeft=Math.Min(0,Math.Floor(local.Min(p=>p.X))),allowedTop=Math.Min(0,Math.Floor(local.Min(p=>p.Y)));
            double allowedRight=Math.Max(width,Math.Ceiling(local.Max(p=>p.X))),allowedBottom=Math.Max(height,Math.Ceiling(local.Max(p=>p.Y)));
            bool whole=Math.Abs(dx-Math.Round(dx))<1e-9&&Math.Abs(dy-Math.Round(dy))<1e-9;
            int radius=whole?0:2;
            double left=Math.Max(allowedLeft,Math.Floor(transfer.Left+dx)-radius),top=Math.Max(allowedTop,Math.Floor(transfer.Top+dy)-radius);
            double right=Math.Min(allowedRight,Math.Ceiling(transfer.Right+dx)+radius),bottom=Math.Min(allowedBottom,Math.Ceiling(transfer.Bottom+dy)+radius);
            if(right<=left||bottom<=top){left=top=0;right=width;bottom=height;}
            int x=(int)Math.Min(0,left),y=(int)Math.Min(0,top);
            var frame=new RasterFrame(x,y,checked((int)Math.Max(width,right)-x),checked((int)Math.Max(height,bottom)-y));
            CheckBudget(frame);
            var expanded=RasterReframe.Apply(source with{Pixels=duplicate?source.Pixels:transfer.CutSource},frame);
            var pixels=transfer.CompositeOnto(expanded.Pixels,dx-frame.Left,dy-frame.Top);
            var next=original.Replace(expanded with{Pixels=pixels}) with{Selection=SelectionGeometry.Move(original.Selection!,nextOffset.X,nextOffset.Y)};
            ValidateNext(next);session.Preview(next);offset=nextOffset;
        }
        catch{Dispose();throw;}
    }
    private void CheckBudget(RasterFrame frame)
    {
        frame.Validate();long total=(long)frame.Width*frame.Height,mask=source.Mask is null?0:total;
        foreach(var layer in original.Layers.Where(l=>l.Id!=source.Id))
        {
            if(!layer.IsAdjustment&&(layer.Pixels.Tiles.Count!=0||layer.Mask is not null))total+=(long)layer.Pixels.Width*layer.Pixels.Height;
            if(layer.Mask is { } m)mask+=(long)m.Pixels.Width*m.Pixels.Height;
        }
        if(total>Limits.MaxPixels||mask>Limits.MaxPixels)throw new InvalidDataException("Moving these pixels exceeds the project's image or mask budget.");
    }
    private void ValidateNext(Document next)
    {
        next.Validate();
        if(EditorSession.UndoBytesRequired(original,next)>EditorSession.MaxHistoryBytes)
            throw new InvalidOperationException("This move exceeds the 256 MiB Undo limit.");
    }
    public void Complete()
    {
        if(finished)return;
        try
        {
            if(offset!=new PointD(0,0))
            {
                var layer=session.Document.Layers.First(l=>l.Id==source.Id);
                var bounds=AlphaBounds(layer.Pixels);
                if(bounds is { } frame)
                {
                    var trimmed=RasterReframe.Apply(layer,frame);
                    var next=session.Document.Replace(trimmed);ValidateNext(next);session.Preview(next);
                }
            }
            session.Commit();finished=true;
        }
        catch{Dispose();throw;}
    }
    private static RasterFrame? AlphaBounds(Raster pixels)
    {
        int left=pixels.Width,top=pixels.Height,right=0,bottom=0;
        foreach(var (key,tile) in pixels.Tiles)
            for(int y=0;y<Math.Min(256,pixels.Height-key.Y*256);y++)
            for(int x=0;x<Math.Min(256,pixels.Width-key.X*256);x++)
                if(tile.Bytes[(y*256+x)*4+3]!=0)
                {left=Math.Min(left,key.X*256+x);top=Math.Min(top,key.Y*256+y);right=Math.Max(right,key.X*256+x+1);bottom=Math.Max(bottom,key.Y*256+y+1);}
        return right>left&&bottom>top?new(left,top,right-left,bottom-top):null;
    }
    public void Dispose(){if(finished)return;finished=true;if(session.InTransaction)session.Cancel();}
}
