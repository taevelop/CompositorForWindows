using System.Runtime.InteropServices;
using Compositor.Core;
using SkiaSharp;
namespace Compositor.Imaging;

public sealed record WandSettings(int Tolerance=32,int SampleRadius=0,bool Contiguous=true,bool SampleAllLayers=false)
{
    public void Validate(){if(Tolerance is <0 or >255||SampleRadius is <0 or >2)throw new ArgumentOutOfRangeException(nameof(Tolerance));}
}
public static class MagicWand
{
    public static DocumentSelection Select(Raster source,PointD point,WandSettings settings,CancellationToken cancellation=default)
    {
        settings.Validate();cancellation.ThrowIfCancellationRequested();
        if(!double.IsFinite(point.X)||!double.IsFinite(point.Y)||point.X<0||point.Y<0||point.X>=source.Width||point.Y>=source.Height)return DocumentSelection.Empty;
        var input=source.ToRgba();var mask=new byte[checked(source.Width*source.Height)];cancellation.ThrowIfCancellationRequested();
        long count=NativePixels.WandMask(input,source.Width,source.Height,(int)Math.Floor(point.X),(int)Math.Floor(point.Y),settings.SampleRadius,settings.Tolerance,settings.Contiguous?1:0,mask);
        cancellation.ThrowIfCancellationRequested();if(count<0)throw new OutOfMemoryException("Not enough memory to select matching pixels.");if(count==0)return DocumentSelection.Empty;
        IntPtr points=IntPtr.Zero,loops=IntPtr.Zero;
        try
        {
            int status=NativePixels.WandTrace(mask,source.Width,source.Height,out points,out long pointCount,out loops,out long loopCount);
            cancellation.ThrowIfCancellationRequested();if(status==-2)throw new InvalidOperationException("Selection is too detailed. Increase tolerance or enable Contiguous.");if(status!=0)throw new OutOfMemoryException("Not enough memory to outline the selection.");
            if(pointCount==0||loopCount==0)return DocumentSelection.Empty;
            if(pointCount>int.MaxValue/2||loopCount>int.MaxValue)throw new InvalidDataException("Invalid native outline size.");
            var coordinates=new int[checked((int)pointCount*2)];var lengths=new int[(int)loopCount];Marshal.Copy(points,coordinates,0,coordinates.Length);Marshal.Copy(loops,lengths,0,lengths.Length);
            using var builder=new SKPathBuilder();int index=0;
            foreach(int length in lengths)
            {
                cancellation.ThrowIfCancellationRequested();if(length<3||length>pointCount-index)throw new InvalidDataException("Invalid native outline loop.");
                var polygon=new SKPoint[length];for(int i=0;i<length;i++)polygon[i]=new(coordinates[(index+i)*2],coordinates[(index+i)*2+1]);builder.AddPoly(polygon,true);index+=length;
            }
            if(index!=pointCount)throw new InvalidDataException("Incomplete native outline.");
            using var path=builder.Detach();var selection=new DocumentSelection(path.ToSvgPathData());selection.Validate();cancellation.ThrowIfCancellationRequested();return selection;
        }
        finally{NativePixels.Free(points);NativePixels.Free(loops);}
    }
}
