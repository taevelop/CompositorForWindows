using Compositor.Core;
namespace Compositor.Imaging;

public static class LayerFill
{
    public static Document Apply(Document document,Guid layerId,byte red,byte green,byte blue,bool editMask=false,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();document.Validate();
        var source=document.Layers.First(layer=>layer.Id==layerId);
        if(source.IsGroup)throw new InvalidOperationException("Select an image or adjustment mask to fill.");
        if(!editMask&&source.IsAdjustment)throw new InvalidOperationException("Adjustment layers have no image pixels.");
        if(editMask&&source.Mask is not{Enabled:true})throw new InvalidOperationException("Select an enabled mask to fill.");
        var selection=document.Selection is {} selected?SelectionCoverage.Create(selected,document.Width,document.Height):null;
        if(selection?.IsEmpty==true)return document;
        var layer=source;
        if(!editMask)
        {
            PointD[] corners=[new(0,0),new(document.Width,0),new(0,document.Height),new(document.Width,document.Height)];
            var points=corners.Select(point=>source.Transform.ToPixels(point,source.Pixels.Width,source.Pixels.Height)).ToArray();
            int left=checked((int)Math.Floor(Math.Min(0,points.Min(p=>p.X)))),top=checked((int)Math.Floor(Math.Min(0,points.Min(p=>p.Y))));
            int right=checked((int)Math.Ceiling(Math.Max(source.Pixels.Width,points.Max(p=>p.X)))),bottom=checked((int)Math.Ceiling(Math.Max(source.Pixels.Height,points.Max(p=>p.Y))));
            layer=RasterReframe.Apply(source,new(left,top,checked(right-left),checked(bottom-top)),cancellation);
        }
        var pixels=editMask?source.Mask!.EditingPixels(source.Pixels.Width,source.Pixels.Height):layer.Pixels;
        if(editMask)green=blue=red;
        var tiles=pixels.Tiles.ToBuilder();bool changed=false;
        for(int ty=0;ty*256<pixels.Height;ty++)for(int tx=0;tx*256<pixels.Width;tx++)
        {
            cancellation.ThrowIfCancellationRequested();var key=new TileKey(tx,ty);tiles.TryGetValue(key,out var before);
            var buffer=before is null?new byte[PixelTile.ByteCount]:before.Bytes.ToArray();
            for(int y=0;y<Math.Min(256,pixels.Height-ty*256);y++)for(int x=0;x<Math.Min(256,pixels.Width-tx*256);x++)
            {
                var point=layer.Transform.ToDocument(new(tx*256+x+.5,ty*256+y+.5),pixels.Width,pixels.Height);
                if(point.X<0||point.Y<0||point.X>=document.Width||point.Y>=document.Height)continue;
                double weight=selection?.Sample(point)??1;if(weight<=0)continue;
                int p=(y*256+x)*4;
                for(int channel=0;channel<4;channel++)
                {int color=channel switch{0=>red,1=>green,2=>blue,_=>255};buffer[p+channel]=(byte)Math.Round(buffer[p+channel]*(1-weight)+color*weight,MidpointRounding.AwayFromZero);}
            }
            if(before is not null&&before.Bytes.SequenceEqual(buffer))continue;
            if(before is null&&buffer.AsSpan().IndexOfAnyExcept((byte)0)<0)continue;
            tiles[key]=new(buffer);changed=true;
        }
        if(!changed)return document;
        var filled=new Raster(pixels.Width,pixels.Height,tiles.ToImmutable());
        var result=document.Replace(editMask?source with{Mask=source.Mask!.WithPixels(filled)}:layer with{Pixels=filled});
        result.Validate();
        if(EditorSession.UndoBytesRequired(document,result)>EditorSession.MaxHistoryBytes)throw new InvalidOperationException("Fill exceeds the Undo memory limit.");
        cancellation.ThrowIfCancellationRequested();return result;
    }
}
