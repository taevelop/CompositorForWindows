using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class WarpRasterStrokeTests
{
    [Fact]
    public void SmudgeCarriesPremultipliedColorAndHonorsSpacingWithoutChangingSource()
    {
        var bytes = new byte[8 * 5 * 4];int sourcePixel = (2 * 8 + 2) * 4;bytes[sourcePixel] = bytes[sourcePixel + 3] = 128;
        var source = Raster.FromRgba(8, 5, bytes);var stroke = new WarpRasterStroke(source, new(2, 1, .5, 0, 0, 0), SmearMode.Smudge);
        stroke.Append(new(2, 2));stroke.Append(new(2.5, 2));Assert.Empty(stroke.Points);Assert.Same(source, stroke.Pixels);
        stroke.Append(new(3, 2));var snapshot = stroke.Pixels;var first = snapshot.ToRgba();int destination = (2 * 8 + 3) * 4;
        Assert.Equal((byte)128, first[destination]);Assert.Equal((byte)128, first[destination + 3]);Assert.Single(stroke.Points);
        stroke.Append(new(4, 2));Assert.Equal(first, snapshot.ToRgba());Assert.Equal(bytes, source.ToRgba());
        var result = stroke.Pixels.ToRgba();for (int p = 0; p < result.Length; p += 4) for (int c = 0; c < 3; c++) Assert.True(result[p + c] <= result[p + 3]);
    }
    [Fact]
    public void LiquifyUsesPreDabBilinearPixelsAcrossTileBoundary()
    {
        var bytes = new byte[513 * 17 * 4];
        for (int y = 0; y < 17; y++) for (int x = 0; x < 513; x++) { int p = (y * 513 + x) * 4;bytes[p] = (byte)Math.Clamp((x - 250) * 20, 0, 255);bytes[p + 3] = 255; }
        var source = Raster.FromRgba(513, 17, bytes);var stroke = new WarpRasterStroke(source, new(4, 1, .5, 0, 0, 0), SmearMode.Liquify);
        stroke.Append(new(255, 8));Assert.Same(source, stroke.Pixels);stroke.Append(new(256, 8));
        int center = (8 * 513 + 256) * 4;var actual = stroke.Pixels.ToRgba();Assert.Equal((byte)110, actual[center]);Assert.Equal((byte)255, actual[center + 3]);
        Assert.Equal(bytes[center + 20], actual[center + 20]);Assert.Equal(bytes, source.ToRgba());
    }
    [Theory]
    [InlineData(SmearMode.Smudge)]
    [InlineData(SmearMode.Liquify)]
    public void CancellationDoesNotPublishAndCancelledStrokeCannotResume(SmearMode mode)
    {
        var source = ShapeRaster.Create(new(ShapeKind.Rectangle, 1, 0, 0), 32, 32);var stroke = new WarpRasterStroke(source, new(10, .7, .8, 0, 0, 0), mode);
        stroke.Append(new(10, 10));Assert.Throws<OperationCanceledException>(() => stroke.Append(new(20, 10), new CancellationToken(true)));
        Assert.Same(source, stroke.Pixels);Assert.Throws<InvalidOperationException>(() => stroke.Append(new(20, 10)));
        Assert.Throws<OperationCanceledException>(() => new WarpRasterStroke(source, new(10, .7, .8, 0, 0, 0), mode, new CancellationToken(true)));
    }
    [Fact]
    public void RejectsInvalidModeAndPointerAndSingleRowLiquifyKeepsPixels()
    {
        var source = ShapeRaster.Create(new(ShapeKind.Rectangle, .5, .2, .1), 8, 1);var stroke = new WarpRasterStroke(source, new(4, .7, 1, 0, 0, 0), SmearMode.Liquify);
        stroke.Append(new(2, 0));stroke.Append(new(3, 0));Assert.Equal(source.ToRgba(), stroke.Pixels.ToRgba());
        Assert.Throws<ArgumentException>(() => stroke.Append(new(double.NaN, 0)));
        Assert.Throws<ArgumentException>(() => new WarpRasterStroke(source, new(4, .7, 1, 0, 0, 0), SmearMode.Blur));
    }
}
