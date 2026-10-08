using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;

public class LensCorrectionTests
{
    [Theory]
    [InlineData(-100)]
    [InlineData(100)]
    public void NativeWarpMatchesIndependentBilinearReference(double distortion)
    {
        const int w = 263, h = 9;
        var bytes = new byte[w * h * 4];
        for (int p = 0; p < w * h; p++)
        {
            bytes[p * 4] = (byte)(p % 128);
            bytes[p * 4 + 1] = (byte)(p % 97);
            bytes[p * 4 + 2] = 32;
            bytes[p * 4 + 3] = 128;
        }
        var source = Raster.FromRgba(w, h, bytes);
        var expected = new byte[bytes.Length];
        double cx = w / 2d, cy = h / 2d;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            double dx = x + .5 - cx, dy = y + .5 - cy;
            double scale = 1 - distortion / 100 * .35 * (dx * dx + dy * dy) / (cx * cx + cy * cy);
            double sx = cx + dx * scale - .5, sy = cy + dy * scale - .5;
            int ix = (int)Math.Floor(sx), iy = (int)Math.Floor(sy);
            for (int c = 0; c < 4; c++)
            {
                double sum = 0;
                for (int j = 0; j < 2; j++)
                for (int i = 0; i < 2; i++)
                    if (ix + i >= 0 && ix + i < w && iy + j >= 0 && iy + j < h)
                        sum += bytes[((iy + j) * w + ix + i) * 4 + c] * (i == 0 ? 1 - (sx - ix) : sx - ix) * (j == 0 ? 1 - (sy - iy) : sy - iy);
                expected[(y * w + x) * 4 + c] = (byte)Math.Round(sum, MidpointRounding.AwayFromZero);
            }
        }
        Assert.Equal(expected, LensCorrection.Apply(source, distortion).ToRgba());
        Assert.Equal(bytes, source.ToRgba());
        Assert.Same(source, LensCorrection.Apply(source, 0));
    }

    [Fact]
    public void InvalidAndCancelledOperationsDoNotChangeSource()
    {
        var source = new Raster(3, 3);
        Assert.Throws<ArgumentOutOfRangeException>(() => LensCorrection.Apply(source, double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => LensCorrection.Apply(source, 101));
        Assert.Throws<OperationCanceledException>(() => LensCorrection.Apply(source, 0, new CancellationToken(true)));
    }
}
