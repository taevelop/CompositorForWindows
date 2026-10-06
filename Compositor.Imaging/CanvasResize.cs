using System.Collections.Immutable;
using Compositor.Core;
namespace Compositor.Imaging;

public enum CanvasUnit { Pixels, Percent, Inches, Centimeters }
public sealed class CanvasSizeDraft(int originalWidth, int originalHeight, double resolution)
{
    public int OriginalWidth { get; } = originalWidth;
    public int OriginalHeight { get; } = originalHeight;
    public double Resolution { get; } = resolution;
    public double Width { get; private set; } = originalWidth;
    public double Height { get; private set; } = originalHeight;
    public bool Relative { get; set; }
    public bool Locked { get; set; }
    public CanvasUnit Unit { get; set; }
    public double Displayed(bool widthAxis)
    {
        double original=widthAxis?OriginalWidth:OriginalHeight;
        double pixels=(widthAxis?Width:Height)-(Relative?original:0);
        return Unit switch { CanvasUnit.Pixels=>pixels,CanvasUnit.Percent=>pixels/original*100,
            CanvasUnit.Inches=>pixels/Resolution,CanvasUnit.Centimeters=>pixels/Resolution*2.54,
            _=>throw new ArgumentOutOfRangeException(nameof(Unit)) };
    }
    public void Set(double value,bool widthAxis)
    {
        if(!double.IsFinite(value))throw new ArgumentOutOfRangeException(nameof(value));
        double original=widthAxis?OriginalWidth:OriginalHeight;
        double pixels=Unit switch { CanvasUnit.Pixels=>value,CanvasUnit.Percent=>value/100*original,
            CanvasUnit.Inches=>value*Resolution,CanvasUnit.Centimeters=>value/2.54*Resolution,
            _=>throw new ArgumentOutOfRangeException(nameof(Unit)) };
        double final=pixels+(Relative?original:0);
        if(widthAxis){Width=final;if(Locked)Height=final*OriginalHeight/OriginalWidth;}
        else {Height=final;if(Locked)Width=final*OriginalWidth/OriginalHeight;}
    }
    public (int Width,int Height) Dimensions()
    {
        double w=Math.Round(Width,MidpointRounding.AwayFromZero),h=Math.Round(Height,MidpointRounding.AwayFromZero);
        if(!double.IsFinite(w)||!double.IsFinite(h)||w<1||h<1||w>30000||h>30000)
            throw new InvalidDataException("Final dimensions must be 1–30,000 pixels per side.");
        Limits.CheckDimensions((int)w,(int)h);return ((int)w,(int)h);
    }
}
public readonly record struct CanvasFill(byte Red,byte Green,byte Blue);
public sealed record CanvasSizeOptions(int Width,int Height,int Anchor=4,CanvasFill? Fill=null)
{
    public PointD Offset(int oldWidth,int oldHeight)
    {
        Limits.CheckDimensions(Width,Height);
        if(Anchor is <0 or >8)throw new ArgumentOutOfRangeException(nameof(Anchor));
        return new(Math.Floor((Width-oldWidth)*(Anchor%3)/2d),Math.Floor((Height-oldHeight)*(Anchor/3)/2d));
    }
}
public static class CanvasResize
{
    public static Document Apply(Document source,CanvasSizeOptions options,CancellationToken cancellationToken=default)
    {
        var offset=options.Offset(source.Width,source.Height);
        if(options.Width==source.Width&&options.Height==source.Height)return source;
        cancellationToken.ThrowIfCancellationRequested();
        var next=CanvasCrop.Apply(source,new(-(int)offset.X,-(int)offset.Y,options.Width,options.Height));
        if(options.Fill is not {} color || (options.Width<=source.Width&&options.Height<=source.Height))return next;
        long used=source.Layers.Where(l=>!l.IsAdjustment&&(l.Pixels.Tiles.Count!=0||l.Mask is not null))
            .Sum(l=>(long)l.Pixels.Width*l.Pixels.Height);
        if((long)options.Width*options.Height>Limits.MaxPixels-used||source.Layers.Length>=10000)
            throw new InvalidDataException("Canvas extension exceeds the project image or layer limit.");
        // Build only the extension tiles; the old canvas intersection stays transparent.
        var tiles=ImmutableDictionary.CreateBuilder<TileKey,PixelTile>();
        for(int y=0;y<options.Height;y+=PixelTile.Side)
        for(int x=0;x<options.Width;x+=PixelTile.Side)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int right=Math.Min(x+PixelTile.Side,options.Width),bottom=Math.Min(y+PixelTile.Side,options.Height);
            if(x>=offset.X&&right<=offset.X+source.Width&&y>=offset.Y&&bottom<=offset.Y+source.Height)continue;
            var bytes=new byte[PixelTile.ByteCount];bool painted=false;
            for(int row=y;row<bottom;row++)
            for(int col=x;col<right;col++)
            {
                if(col>=offset.X&&col<offset.X+source.Width&&row>=offset.Y&&row<offset.Y+source.Height)continue;
                int i=(row-y)*PixelTile.Stride+(col-x)*4;
                bytes[i]=color.Red;bytes[i+1]=color.Green;bytes[i+2]=color.Blue;bytes[i+3]=255;painted=true;
            }
            if(painted)tiles[new(x/PixelTile.Side,y/PixelTile.Side)]=new(bytes);
        }
        var extension=Layer.Blank("Canvas Extension",options.Width,options.Height) with
        {Pixels=new(options.Width,options.Height,tiles.ToImmutable())};
        next=next with{Layers=next.Layers.Insert(0,extension)};next.Validate();return next;
    }
}
