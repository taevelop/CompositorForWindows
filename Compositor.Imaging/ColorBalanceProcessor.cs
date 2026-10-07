using System.Runtime.InteropServices;
using Compositor.Core;
namespace Compositor.Imaging;

internal static class ColorBalanceProcessor
{
    [DllImport("Compositor.Native",EntryPoint="compositor_color_balance",CallingConvention=CallingConvention.Cdecl)]
    private static extern void ApplyRange(IntPtr pixels,int count,float[] shadows,float[] midtones,float[] highlights,int preserve);

    internal static void Apply(byte[] pixels,ColorBalanceAdjustment settings,int? workers=null,CancellationToken cancellationToken=default)
    {
        if(pixels.Length%4!=0)throw new ArgumentException("Expected packed RGBA pixels.",nameof(pixels));
        cancellationToken.ThrowIfCancellationRequested();settings.Validate();
        var shadows=settings.Shadows;var midtones=settings.Midtones;var highlights=settings.Highlights;
        int count=pixels.Length/4,degree=Math.Clamp(workers??Environment.ProcessorCount,1,8);
        if(count<262144&&!cancellationToken.CanBeCanceled)
        {NativePixels.ColorBalance(pixels,count,shadows,midtones,highlights,settings.PreserveLuminosity?1:0);return;}
        // Pixels are independent in the original kernel. Pin once and partition into
        // disjoint complete pixels; retain the pin until every native call has returned.
        var pin=GCHandle.Alloc(pixels,GCHandleType.Pinned);
        try
        {
            var address=pin.AddrOfPinnedObject();
            Parallel.For(0,(count+65535)/65536,new ParallelOptions{MaxDegreeOfParallelism=degree,CancellationToken=cancellationToken},index=>
            {
                cancellationToken.ThrowIfCancellationRequested();
                int start=index*65536,end=Math.Min(count,start+65536);
                ApplyRange(IntPtr.Add(address,checked(start*4)),end-start,shadows,midtones,highlights,settings.PreserveLuminosity?1:0);
            });
        }
        finally{pin.Free();}
    }
}
