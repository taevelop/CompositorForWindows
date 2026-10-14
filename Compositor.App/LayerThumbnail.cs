using System.Runtime.CompilerServices;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
namespace Compositor.App;

internal static class LayerThumbnail
{
    private static readonly ConditionalWeakTable<Raster,BitmapSource> cache=new();
    internal static BitmapSource Get(Raster raster)=>cache.GetValue(raster,Create);
    private static BitmapSource Create(Raster source)
    {
        const int side=32;var bytes=new byte[side*side*4];
        double scale=Math.Min((double)side/source.Width,(double)side/source.Height);
        int width=Math.Max(1,(int)Math.Round(source.Width*scale)),height=Math.Max(1,(int)Math.Round(source.Height*scale));
        int left=(side-width)/2,top=(side-height)/2;
        byte At(int x,int y,int c)
        {
            x=Math.Clamp(x,0,source.Width-1);y=Math.Clamp(y,0,source.Height-1);
            return source.Tiles.TryGetValue(new(x/256,y/256),out var tile)?tile.Bytes[((y%256)*256+x%256)*4+c]:(byte)0;
        }
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            double sx=(x+.5)*source.Width/width-.5,sy=(y+.5)*source.Height/height-.5;int x0=(int)Math.Floor(sx),y0=(int)Math.Floor(sy);double fx=sx-x0,fy=sy-y0;
            for(int c=0;c<4;c++)
            {
                double value=(At(x0,y0,c)*(1-fx)+At(x0+1,y0,c)*fx)*(1-fy)+(At(x0,y0+1,c)*(1-fx)+At(x0+1,y0+1,c)*fx)*fy;
                int channel=c==0?2:c==2?0:c;bytes[((top+y)*side+left+x)*4+channel]=(byte)Math.Clamp(Math.Round(value),0,255);
            }
        }
        var image=BitmapSource.Create(side,side,96,96,PixelFormats.Pbgra32,null,bytes,side*4);image.Freeze();return image;
    }
}
