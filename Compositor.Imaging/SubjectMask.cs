using System.Collections.Immutable;
using Compositor.Core;

namespace Compositor.Imaging;

/// <summary>Applies a full-resolution inference matte without changing image pixels.</summary>
public static class SubjectMask
{
    public static Document Apply(Document document, Guid layerId, LayerMask subject, CancellationToken cancellation = default)
    {
        document.Validate(); cancellation.ThrowIfCancellationRequested();
        var layer = document.Layers.First(l => l.Id == layerId);
        if (layer.IsGroup || layer.IsAdjustment) throw new InvalidOperationException("Select an image layer.");
        int width = layer.Pixels.Width, height = layer.Pixels.Height;
        if (subject.Pixels.Width != width || subject.Pixels.Height != height) throw new ArgumentException("Subject mask must match the image grid.", nameof(subject));
        var existing = layer.Mask;
        var resolved = existing is null ? LayerMask.Solid(width,height) :
            existing.Placement is null && existing.Pixels.Width == width && existing.Pixels.Height == height ? existing :
            MaskPlacement.Resolve(existing, existing.Placement ?? layer.Transform, layer.Transform, width, height, cancellation);
        var basePixels = resolved.EditingPixels(width,height);
        var selection = document.Selection is null ? null : SelectionCoverage.Create(document.Selection,document.Width,document.Height);
        if (selection?.IsEmpty == true) return document;
        var tiles = ImmutableDictionary.CreateBuilder<TileKey,PixelTile>();
        foreach (var (key,tile) in subject.Pixels.Tiles)
        {
            cancellation.ThrowIfCancellationRequested();
            var bytes = basePixels.Tiles[key].Bytes.ToArray();
            for (int y=0;y<Math.Min(256,height-key.Y*256);y++)
            for (int x=0;x<Math.Min(256,width-key.X*256);x++)
            {
                int i=(y*256+x)*4;
                double weight=selection?.Sample(layer.Transform.ToDocument(new(key.X*256+x+.5,key.Y*256+y+.5),width,height))??1;
                byte gray=(byte)Math.Round(bytes[i]*(1-weight)+bytes[i]*tile.Bytes[i]/255d*weight,MidpointRounding.AwayFromZero);
                bytes[i]=bytes[i+1]=bytes[i+2]=gray;
            }
            tiles[key]=new(bytes);
        }
        var mask = new LayerMask(new Raster(width,height,tiles.ToImmutable())) { Linked=existing?.Linked??true, Placement=existing?.Linked==false?layer.Transform:null };
        var result=document.Replace(layer with {Mask=mask});result.Validate();
        if(EditorSession.UndoBytesRequired(document,result)>EditorSession.MaxHistoryBytes)throw new InvalidOperationException("Subject mask exceeds the Undo memory limit.");
        cancellation.ThrowIfCancellationRequested();return result;
    }
}
