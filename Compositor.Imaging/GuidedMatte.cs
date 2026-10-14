using Compositor.Core;

namespace Compositor.Imaging;

/// <summary>Original scalar guided-filter arithmetic with clamped image edges.</summary>
public static class GuidedMatte
{
    internal static float[] Box(float[] source,int width,int height,int radius,CancellationToken cancellation=default)
    {
        Limits.CheckDimensions(width,height);
        if(source.Length!=checked(width*height)||radius is <1 or >250)throw new ArgumentException("Invalid guided filter dimensions or radius.");
        var pass=new float[source.Length];var output=new float[source.Length];float span=radius*2+1;
        for(int y=0;y<height;y++)
        {
            cancellation.ThrowIfCancellationRequested();int row=y*width;float sum=0;
            for(int x=-radius;x<=radius;x++)sum+=source[row+Math.Clamp(x,0,width-1)];
            for(int x=0;x<width;x++){pass[row+x]=sum/span;sum-=source[row+Math.Clamp(x-radius,0,width-1)];sum+=source[row+Math.Clamp(x+radius+1,0,width-1)];}
        }
        for(int x=0;x<width;x++)
        {
            cancellation.ThrowIfCancellationRequested();float sum=0;
            for(int y=-radius;y<=radius;y++)sum+=pass[Math.Clamp(y,0,height-1)*width+x];
            for(int y=0;y<height;y++){output[y*width+x]=sum/span;sum-=pass[Math.Clamp(y-radius,0,height-1)*width+x];sum+=pass[Math.Clamp(y+radius+1,0,height-1)*width+x];}
        }
        return output;
    }
    public static float[] Filter(float[] mask,float[] guide,int width,int height,int radius,CancellationToken cancellation=default)
    {
        Limits.CheckDimensions(width,height);
        if(mask.Length!=checked(width*height)||guide.Length!=mask.Length||radius is <1 or >250||mask.Any(v=>!float.IsFinite(v)||v<0||v>1)||guide.Any(v=>!float.IsFinite(v)||v<0||v>1))throw new ArgumentException("Use finite 0–1 mask and guide levels with radius 1–250.");
        cancellation.ThrowIfCancellationRequested();
        if(mask.Length<=65536)return FilterCore(mask,guide,width,height,radius,cancellation);
        // Two box passes in the guided filter reach at most 2r from each output.
        // Keep that halo so internal chunk boundaries never become clamped image edges.
        var output=new float[mask.Length];int halo=radius*2;
        for(int top=0;top<height;top+=256)for(int left=0;left<width;left+=256)
        {
            cancellation.ThrowIfCancellationRequested();
            int x0=Math.Max(0,left-halo),y0=Math.Max(0,top-halo);
            int right=Math.Min(width,left+256+halo),bottom=Math.Min(height,top+256+halo);
            int w=right-x0,h=bottom-y0;var localMask=new float[w*h];var localGuide=new float[w*h];
            for(int y=0;y<h;y++)
            {
                cancellation.ThrowIfCancellationRequested();
                mask.AsSpan((y+y0)*width+x0,w).CopyTo(localMask.AsSpan(y*w));
                guide.AsSpan((y+y0)*width+x0,w).CopyTo(localGuide.AsSpan(y*w));
            }
            var local=FilterCore(localMask,localGuide,w,h,radius,cancellation);
            int rows=Math.Min(256,height-top),columns=Math.Min(256,width-left);
            for(int y=0;y<rows;y++)local.AsSpan((top-y0+y)*w+left-x0,columns).CopyTo(output.AsSpan((top+y)*width+left,columns));
        }
        cancellation.ThrowIfCancellationRequested();return output;
    }
    internal static float[] FilterCore(float[] mask,float[] guide,int width,int height,int radius,CancellationToken cancellation=default)
    {
        var meanGuide=Box(guide,width,height,radius,cancellation);var meanMask=Box(mask,width,height,radius,cancellation);
        var squares=new float[mask.Length];var products=new float[mask.Length];
        for(int i=0;i<mask.Length;i++){squares[i]=guide[i]*guide[i];products[i]=guide[i]*mask[i];}
        var meanSquares=Box(squares,width,height,radius,cancellation);var meanProducts=Box(products,width,height,radius,cancellation);
        var slope=new float[mask.Length];var offset=new float[mask.Length];
        for(int i=0;i<mask.Length;i++)
        {
            if(i%65536==0)cancellation.ThrowIfCancellationRequested();
            slope[i]=(meanProducts[i]-meanGuide[i]*meanMask[i])/(meanSquares[i]-meanGuide[i]*meanGuide[i]+1e-4f);
            offset[i]=meanMask[i]-slope[i]*meanGuide[i];
        }
        var meanSlope=Box(slope,width,height,radius,cancellation);var meanOffset=Box(offset,width,height,radius,cancellation);
        var result=new float[mask.Length];
        for(int i=0;i<result.Length;i++){if(i%65536==0)cancellation.ThrowIfCancellationRequested();result[i]=Math.Clamp(meanSlope[i]*guide[i]+meanOffset[i],0,1);}
        return result;
    }
}
