using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;

public class CloneStampTests
{
    [Fact]
    public void AlignmentTracksStrokesAndNewSourceResetsIt()
    {
        var source = new CloneSource();
        Assert.Null(source.OffsetAt(new(1, 1), true));
        Assert.Throws<InvalidOperationException>(() => source.Begin(new(1, 1), true));
        source.Set(new(10.5, 20.5));
        Assert.Equal(new PointD(10, 20), source.Begin(new(1, 1), true));
        Assert.Equal(new PointD(10, 20), source.Begin(new(4, 5), true));
        Assert.Equal(new PointD(14, 25), source.SamplePoint(new(4, 5), true, false));
        Assert.Equal(new PointD(7, 16), source.Begin(new(4, 5), false));
        Assert.Equal(new PointD(10.5, 20.5), source.SamplePoint(new(8, 9), false, false));
        Assert.Equal(new PointD(15, 25), source.SamplePoint(new(8, 9), false, true));
        source.Set(new(0, 0));
        Assert.Equal(new PointD(-1, -1), source.Begin(new(.5, .5), true));
        source.Set(new(double.NaN, 0)); Assert.Equal(new PointD(0, 0), source.Source);
    }

    [Fact]
    public void CaptureFreezesRawPixelsAndSeparatesCompositeSampling()
    {
        var document = Document.Create(2, 2);
        var pixels = Raster.FromRgba(2, 2, Enumerable.Repeat(new byte[] { 80, 20, 10, 255 }, 4).SelectMany(x => x).ToArray());
        var layer = document.Layers[0] with { Pixels = pixels, Opacity = 0 };
        document = document.Replace(layer);
        var raw = CloneStamp.Capture(document, layer.Id, new());
        Assert.Equal(pixels.ToRgba(), raw.ToRgba());
        Assert.All(CloneStamp.Capture(document, layer.Id, new(SampleAllLayers: true)).ToRgba(), b => Assert.Equal((byte)0, b));
        Assert.Equal(pixels.ToRgba(), raw.ToRgba()); Assert.Same(pixels, document.Layers[0].Pixels);
        Assert.Throws<OperationCanceledException>(() => CloneStamp.Capture(document, layer.Id, new(), new CancellationToken(true)));
    }
}
