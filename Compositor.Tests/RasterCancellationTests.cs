using Compositor.Core;
using Xunit;

namespace Compositor.Tests;
public class RasterCancellationTests
{
    [Fact]
    public void CancelledConversionDoesNotChangeItsInput()
    {
        var bytes = Enumerable.Repeat((byte)127, 257 * 263 * 4).ToArray();var before = (byte[])bytes.Clone();
        Assert.Throws<OperationCanceledException>(() => Raster.FromRgba(257, 263, bytes, new CancellationToken(true)));
        Assert.Equal(before, bytes);
        var result = Raster.FromRgba(257, 263, bytes, CancellationToken.None);Assert.Equal(before, result.ToRgba());
        bytes[0] = 0;Assert.Equal((byte)127, result.ToRgba()[0]);
    }
}
