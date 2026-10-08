using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;

public class ContentAwareFillTests
{
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
