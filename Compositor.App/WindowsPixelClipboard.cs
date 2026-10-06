using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;

namespace Compositor.App;

internal sealed class WindowsPixelClipboard
{
    private readonly Func<IDataObject?> read;
    private readonly Action<IDataObject> write;
    private readonly Func<uint> sequence;
    private PixelClipboardContent? cached;
    private uint cachedSequence;
    private string? ownershipToken;
    private const string OwnershipFormat="Compositor.PixelClipboard.Owner.v1";
    [DllImport("user32.dll")] private static extern uint GetClipboardSequenceNumber();
    public WindowsPixelClipboard():this(Clipboard.GetDataObject,data=>Clipboard.SetDataObject(data,true),GetClipboardSequenceNumber){}
    internal WindowsPixelClipboard(Func<IDataObject?> read,Action<IDataObject> write,Func<uint> sequence)
    {this.read=read;this.write=write;this.sequence=sequence;}
    public void Write(PixelClipboardContent content)
    {
        var pixels=content.Pixels;var rgba=pixels.ToRgba();
        using var bitmap=new SKBitmap(CanvasRenderer.Info(pixels.Width,pixels.Height));rgba.CopyTo(bitmap.GetPixelSpan());
        using var image=SKImage.FromBitmap(bitmap);using var png=image.Encode(SKEncodedImageFormat.Png,100)??throw new IOException("PNG encoding failed.");
        var data=new DataObject();data.SetData("PNG",new MemoryStream(png.ToArray()),false);
        for(int p=0;p<rgba.Length;p+=4)(rgba[p],rgba[p+2])=(rgba[p+2],rgba[p]);
        var compatible=BitmapSource.Create(pixels.Width,pixels.Height,96,96,PixelFormats.Pbgra32,null,rgba,pixels.Width*4);compatible.Freeze();
        data.SetData(DataFormats.Bitmap,compatible,true);
        string token=Guid.NewGuid().ToString("N");data.SetData(OwnershipFormat,token,false);
        write(data);cached=content;ownershipToken=token;cachedSequence=sequence();
    }
    public (PixelClipboardContent Content,bool PreserveOrigin)? Read()
    {
        uint before=sequence();
        var data=read();if(data is null)return null;
        if(before!=0&&before==cachedSequence&&cached is not null&&data.GetData(OwnershipFormat,false) is string token&&token==ownershipToken)return(cached,true);
        if(data.GetDataPresent("PNG",false))
        {
            var value=data.GetData("PNG",false);
            byte[] encoded;
            if(value is byte[] bytes)encoded=bytes;
            else if(value is Stream stream)
            {
                if(stream.CanSeek){if(stream.Length>ImageCodec.MaxEncodedBytes)throw new InvalidDataException("Clipboard image exceeds 512 MiB.");stream.Position=0;}
                using var copy=new MemoryStream();var buffer=new byte[81920];int count;
                while((count=stream.Read(buffer))>0){if(copy.Length+count>ImageCodec.MaxEncodedBytes)throw new InvalidDataException("Clipboard image exceeds 512 MiB.");copy.Write(buffer,0,count);}
                encoded=copy.ToArray();
            }
            else throw new InvalidDataException("Unsupported clipboard PNG representation.");
            if(encoded.LongLength>ImageCodec.MaxEncodedBytes)throw new InvalidDataException("Clipboard image exceeds 512 MiB.");
            using var streamData=new SKMemoryStream(encoded);using var codec=SKCodec.Create(streamData)??throw new InvalidDataException("Invalid clipboard PNG.");
            if(codec.EncodedFormat!=SKEncodedImageFormat.Png)throw new InvalidDataException("Clipboard PNG format mismatch.");
            Limits.CheckDimensions(codec.Info.Width,codec.Info.Height);
            using var bitmap=new SKBitmap(CanvasRenderer.Info(codec.Info.Width,codec.Info.Height));
            if(codec.GetPixels(bitmap.Info,bitmap.GetPixels())!=SKCodecResult.Success)throw new InvalidDataException("Incomplete clipboard PNG.");
            return(new(Raster.FromRgba(bitmap.Width,bitmap.Height,bitmap.GetPixelSpan()),new(0,0)),false);
        }
        if(data.GetData(DataFormats.Bitmap,true) is not BitmapSource source)return null;
        Limits.CheckDimensions(source.PixelWidth,source.PixelHeight);
        if(source is BitmapFrame frame&&frame.ColorContexts is {Count:>0} contexts)
            source=new ColorConvertedBitmap(source,contexts[0],new ColorContext(PixelFormats.Bgra32),PixelFormats.Pbgra32);
        var converted=new FormatConvertedBitmap(source,PixelFormats.Pbgra32,null,0);
        var rgba=new byte[checked(source.PixelWidth*source.PixelHeight*4)];converted.CopyPixels(rgba,source.PixelWidth*4,0);
        for(int p=0;p<rgba.Length;p+=4)(rgba[p],rgba[p+2])=(rgba[p+2],rgba[p]);
        return(new(Raster.FromRgba(source.PixelWidth,source.PixelHeight,rgba),new(0,0)),false);
    }
}
