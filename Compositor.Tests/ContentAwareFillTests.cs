using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;

public class ContentAwareFillTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(37, false)]
    [InlineData(-23, true)]
    public void FeatherUsesDocumentCoordinatesAndLeavesZeroCoveragePixelsExact(double rotation, bool flip)
    {
        const int size = 40;
        var bytes = new byte[size * size * 4];
        var transform = new LayerTransform(16,16,size,size,Rotation:rotation,FlipX:flip);
        var selection = SelectionGeometry.Box(30,30,10,10) with { Feather = 4 };
        var coverage = SelectionCoverage.Create(selection,80,80);
        var mask = new byte[size * size];
        for (int y=0;y<size;y++) for (int x=0;x<size;x++)
        {
            int p=y*size+x;
            double weight=coverage.Sample(transform.ToDocument(new(x+.5,y+.5),size,size));
            mask[p]=(byte)Math.Round(weight*255,MidpointRounding.AwayFromZero);
            bytes[p*4]=(byte)(mask[p]>0?0:80);bytes[p*4+1]=(byte)(mask[p]>0?0:100);bytes[p*4+2]=(byte)(mask[p]>0?0:120);bytes[p*4+3]=255;
        }
        var source=Raster.FromRgba(size,size,bytes);
        var synthesized=ContentAwareFill.Apply(source,mask).ToRgba();
        var doc=Document.Create(80,80);var layer=doc.Layers[0] with {Pixels=source,Transform=transform};doc=doc.Replace(layer) with {Selection=selection};
        var output=ContentAwareFill.Apply(doc,layer.Id).Layers[0];
        Assert.Equal(transform,output.Transform);
        var actual=output.Pixels.ToRgba();
        bool fractional=false;
        for(int y=0;y<size;y++)for(int x=0;x<size;x++)
        {
            double weight=coverage.Sample(transform.ToDocument(new(x+.5,y+.5),size,size));
            fractional|=weight>0&&weight<1;
            for(int c=0;c<4;c++)
            {
                int i=(y*size+x)*4+c;
                Assert.Equal((byte)Math.Round(bytes[i]*(1-weight)+synthesized[i]*weight,MidpointRounding.AwayFromZero),actual[i]);
            }
        }
        Assert.True(fractional);Assert.Equal(bytes,source.ToRgba());
    }

    [Fact]
    public void DocumentFillGrowsToSelectionPreservesMaskAndUndo()
    {
        var doc = Document.Create(40, 40);
        var layer = doc.Layers[0] with { Pixels = ShapeRaster.Create(new(ShapeKind.Rectangle,.2,.4,.6),24,24), Transform = new(4,4,24,24), Mask = LayerMask.Solid(24,24), Opacity = .6 };
        doc = doc.Replace(layer) with { Selection = SelectionGeometry.Box(24,12,32,20) };
        var result = ContentAwareFill.Apply(doc, layer.Id);
        Assert.True(result.Layers[0].Pixels.Width > 24);
        Assert.Equal(.6, result.Layers[0].Opacity);
        Assert.NotNull(result.Layers[0].Mask);
        var session = new EditorSession(doc); session.Begin(); session.Preview(result); session.Commit();
        session.Undo(); Assert.Same(doc, session.Document);
        session.Redo(); Assert.Same(result, session.Document);
        Assert.Same(layer.Pixels, doc.Layers[0].Pixels);
    }

    [Fact]
    public void ConstantSurroundingsRepairSelectionAndLeaveOtherPixelsUntouched()
    {
        const int w = 263, h = 16;
        var bytes = new byte[w * h * 4];
        var mask = new byte[w * h];
        for (int p = 0; p < mask.Length; p++)
        {
            bytes[p * 4] = 50; bytes[p * 4 + 1] = 80; bytes[p * 4 + 2] = 100; bytes[p * 4 + 3] = 255;
            if (p % w >= 254 && p % w <= 258 && p / w >= 6 && p / w <= 8)
            { mask[p] = 255; bytes[p * 4] = 0; bytes[p * 4 + 1] = 0; bytes[p * 4 + 2] = 0; }
        }
        // An unselected transparent pixel must never become a donor or be modified.
        Array.Clear(bytes, 0, 4);
        var source = Raster.FromRgba(w, h, bytes);
        var after = ContentAwareFill.Apply(source, mask).ToRgba();
        for (int p = 0; p < mask.Length; p++)
            Assert.Equal(mask[p] == 0 ? bytes.AsSpan(p * 4, 4).ToArray() : new byte[] { 50, 80, 100, 255 }, after.AsSpan(p * 4, 4).ToArray());
        Assert.Equal(bytes, source.ToRgba());
        Assert.Equal(after, ContentAwareFill.Apply(source, mask).ToRgba());
        Assert.Same(source, ContentAwareFill.Apply(source, new byte[mask.Length]));
    }

    [Fact]
    public void MissingDonorsInvalidMaskAndCancellationFailWithoutChangingSource()
    {
        var source = new Raster(8, 8);
        var mask = Enumerable.Repeat((byte)255, 64).ToArray();
        Assert.Throws<InvalidOperationException>(() => ContentAwareFill.Apply(source, mask));
        Assert.Throws<ArgumentException>(() => ContentAwareFill.Apply(source, new byte[1]));
        Assert.Throws<OperationCanceledException>(() => ContentAwareFill.Apply(source, mask, new CancellationToken(true)));
        Assert.Empty(source.Tiles);
    }
}


