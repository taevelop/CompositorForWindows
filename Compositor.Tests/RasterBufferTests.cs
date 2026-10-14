using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class RasterBufferTests
{
    [Fact]
    public void DirectCopyMatchesPackedPixelsAcrossPartialAndMissingTiles()
    {
        const int width=519,height=271;var bytes=new byte[width*height*4];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(x>=256)
        {int p=(y*width+x)*4;bytes[p]=(byte)(x%128);bytes[p+1]=(byte)(y%128);bytes[p+2]=50;bytes[p+3]=128;}
        var source=Raster.FromRgba(width,height,bytes);var after=new byte[bytes.Length];Array.Fill(after,(byte)239);
        RasterBuffer.CopyTo(source,after);Assert.Equal(bytes,after);Assert.Equal(bytes,source.ToRgba());
        Assert.Throws<ArgumentException>(()=>RasterBuffer.CopyTo(source,new byte[1]));
        Assert.Throws<OperationCanceledException>(()=>RasterBuffer.CopyTo(source,after,new CancellationToken(true)));
    }
}
