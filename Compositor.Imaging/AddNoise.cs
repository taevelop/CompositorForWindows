using Compositor.Core;
namespace Compositor.Imaging;
public enum NoiseDistribution {Uniform,Gaussian}
public sealed record NoiseSettings(double Amount=10,NoiseDistribution Distribution=NoiseDistribution.Uniform,bool Monochromatic=false,uint Seed=1)
{
    public void Validate()
    {
        if(!double.IsFinite(Amount)||Amount<.1||Amount>400||!Enum.IsDefined(Distribution))throw new ArgumentOutOfRangeException(nameof(Amount));
    }
}
public static class AddNoise
{
    public static Raster Apply(Raster source,NoiseSettings settings,CancellationToken cancellation=default)
    {
        settings.Validate();cancellation.ThrowIfCancellationRequested();if(source.Tiles.Count==0)return source;
        var pixels=source.ToRgba();cancellation.ThrowIfCancellationRequested();
        NativePixels.AddNoise(pixels,source.Width,source.Height,(float)settings.Amount,settings.Distribution==NoiseDistribution.Gaussian?1:0,settings.Monochromatic?1:0,settings.Seed);
        cancellation.ThrowIfCancellationRequested();var tiles=source.Tiles.ToBuilder();bool changed=false;var buffer=new byte[PixelTile.ByteCount];
        foreach(var (key,tile) in source.Tiles)
        {
            cancellation.ThrowIfCancellationRequested();Array.Clear(buffer);
            int width=Math.Min(256,source.Width-key.X*256),height=Math.Min(256,source.Height-key.Y*256);
            for(int y=0;y<height;y++)pixels.AsSpan(((key.Y*256+y)*source.Width+key.X*256)*4,width*4).CopyTo(buffer.AsSpan(y*PixelTile.Stride));
            if(tile.Bytes.SequenceEqual(buffer))continue;
            tiles[key]=new(buffer);changed=true;
        }
        cancellation.ThrowIfCancellationRequested();return changed?new(source.Width,source.Height,tiles.ToImmutable()):source;
    }
    public static Document Apply(Document document,Guid layerId,NoiseSettings settings,CancellationToken cancellation=default)
    {
        document.Validate();settings.Validate();cancellation.ThrowIfCancellationRequested();
        var source=document.Layers.First(l=>l.Id==layerId);
        if(source.IsGroup||source.IsAdjustment)throw new InvalidOperationException("Select image pixels to add noise.");
        var selection=document.Selection is null?null:SelectionCoverage.Create(document.Selection,document.Width,document.Height);
        if(selection?.IsEmpty==true)return document;
        var pixels=SelectionPixels.Blend(source.Pixels,Apply(source.Pixels,settings,cancellation),source.Transform,selection,cancellation);
        if(ReferenceEquals(pixels,source.Pixels))return document;
        var next=document.Replace(source with{Pixels=pixels});next.Validate();
        if(EditorSession.UndoBytesRequired(document,next)>EditorSession.MaxHistoryBytes)throw new InvalidOperationException("Noise exceeds the 256 MiB Undo limit.");
        cancellation.ThrowIfCancellationRequested();return next;
    }
}
