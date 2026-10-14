using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;

public class CloneStampTests
{
    [Fact]
    public void PreviewAppliesTipOpacityAndBoundsItsScreenResolution()
    {
        var doc = Document.Create(8, 8);
        var rgba = Enumerable.Repeat(new byte[] { 100, 40, 20, 128 }, 64).SelectMany(x => x).ToArray();
        doc = doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(8, 8, rgba), Opacity = 0 });
        var brush = new BrushSettings(3, 1, .5, 0, 0, 0);
        var preview = CloneStamp.Preview(doc, doc.Layers[0].Id, new(4, 4), new(), brush, 1).ToRgba();
        Assert.Equal(new byte[] { 50, 20, 10, 64 }, preview.Skip(16).Take(4).ToArray());
        Assert.Equal((byte)64, preview[3]);
        Assert.All(CloneStamp.Preview(doc, doc.Layers[0].Id, new(4, 4), new(SampleAllLayers: true), brush, 1).ToRgba(), b => Assert.Equal((byte)0, b));
        var capped = CloneStamp.Preview(doc, doc.Layers[0].Id, new(4, 4), new(), brush with { Diameter = 2000 }, 32);
        Assert.Equal(1024, capped.Width); Assert.Equal(1024, capped.Height);
        Assert.Equal(rgba, doc.Layers[0].Pixels.ToRgba());
        Assert.Throws<OperationCanceledException>(() => CloneStamp.Preview(doc, doc.Layers[0].Id, new(4, 4), new(), brush, 1, new CancellationToken(true)));
    }

    [Fact]
    public void SelectionClipsCloneAndFractionalOffsetsInterpolatePremultipliedPixels()
    {
        var document = Document.Create(4, 2);var layer = document.Layers[0];
        var bytes = new byte[32];bytes[0] = 100;bytes[3] = 128;
        var selection = SelectionCoverage.Create(new DocumentSelection("M2 0L3 0L3 1L2 1Z") { Antialiased = false }, 4, 2);
        var stroke = new BrushStroke(layer, new(3, 1, 1, 0, 0, 0), 4, 2, selection, Raster.FromRgba(4, 2, bytes), new(-1.5, 0));
        stroke.Append(new(2.5, .5));var result = stroke.Pixels.ToRgba();
        Assert.Equal(new byte[] { 50, 0, 0, 64 }, result.Skip(8).Take(4).ToArray());
        for (int p = 0; p < 8; p++) if (p != 2) Assert.Equal((byte)0, result[p * 4 + 3]);
    }

    [Fact]
    public void StrokeUsesFrozenSampleSourceOverAndDoesNotAccumulateOpacity()
    {
        var document = Document.Create(4, 2);
        var original = Enumerable.Repeat(new byte[] { 255, 255, 255, 255 }, 8).SelectMany(x => x).ToArray();
        var layer = document.Layers[0] with { Pixels = Raster.FromRgba(4, 2, original) };
        var sampleBytes = new byte[32];sampleBytes[0] = 100;sampleBytes[3] = 128;
        var sample = Raster.FromRgba(4, 2, sampleBytes);
        var stroke = new BrushStroke(layer, new(1, 1, .5, 255, 255, 255), 4, 2, cloneSample: sample, cloneOffset: new(-2, 0));
        stroke.Append(new(2.5, .5));var result = stroke.Pixels.ToRgba();
        Assert.Equal(new byte[] { 241, 191, 191, 255 }, result.Skip(8).Take(4).ToArray());
        stroke.Append(new(2.5, .5));Assert.Equal(result, stroke.Pixels.ToRgba());
        Assert.Equal(original, layer.Pixels.ToRgba());Assert.Equal(sampleBytes, sample.ToRgba());
        var transparent = new BrushStroke(layer, new(1, 1, 1, 0, 0, 0), 4, 2, cloneSample: sample, cloneOffset: new(100, 0));
        transparent.Append(new(2.5, .5));Assert.Same(layer.Pixels, transparent.Pixels);
    }

    [Fact]
    public void CloneCrossesTileBoundaryAndOnlyChangesSweptPixels()
    {
        var document = Document.Create(260, 2);var layer = document.Layers[0];
        var bytes = Enumerable.Repeat(new byte[] { 60, 20, 10, 255 }, 520).SelectMany(x => x).ToArray();
        var stroke = new BrushStroke(layer, new(1, 1, 1, 0, 0, 0), 260, 2, cloneSample: Raster.FromRgba(260, 2, bytes));
        stroke.Append(new(254.5, .5));stroke.Append(new(257.5, .5));var result = stroke.Pixels.ToRgba();
        for (int x = 254; x <= 257; x++) Assert.Equal(new byte[] { 60, 20, 10, 255 }, result.Skip(x * 4).Take(4).ToArray());
        Assert.Equal((byte)0, result[253 * 4 + 3]);Assert.Equal((byte)0, result[258 * 4 + 3]);Assert.Empty(layer.Pixels.Tiles);
    }

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
