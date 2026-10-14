using Compositor.Core;
using Compositor.Imaging;
using Xunit;

namespace Compositor.Tests;
public class SpotHealingTests
{
    [Fact]
    public void DocumentHealingClipsFinalOutputPreservesMetadataAndSupportsUndoAndStorage()
    {
        var doc = Document.Create(32, 32);var bytes = Enumerable.Repeat(new byte[] { 80, 120, 160, 255 }, 1024).SelectMany(p => p).ToArray();
        int index = 16 * 32 + 16;bytes[index * 4] = bytes[index * 4 + 1] = bytes[index * 4 + 2] = 0;
        var layer = doc.Layers[0] with { Pixels = Raster.FromRgba(32, 32, bytes), Mask = LayerMask.Solid(32, 32), Opacity = .5, Effects = new(ColorOverlay: new(.1, .2, .3, .4)) };
        doc = doc.Replace(layer) with { Selection = new("M16 16L17 16L17 17L16 17Z") { Antialiased = false } };
        var mask = new byte[1024];mask[index] = 255;
        var result = SpotHealing.Compute(doc, layer.Id, mask);var healed = result.Layers[0];
        Assert.Equal(new byte[] { 80, 120, 160, 255 }, healed.Pixels.ToRgba().Skip(index * 4).Take(4).ToArray());
        Assert.Same(layer.Mask, healed.Mask);Assert.Same(layer.Effects, healed.Effects);Assert.Equal(layer.Transform, healed.Transform);Assert.Equal(.5, healed.Opacity);Assert.Equal(doc.Selection, result.Selection);
        var excluded = doc with { Selection = new("M0 0L1 0L1 1L0 1Z") { Antialiased = false } };
        Assert.Same(excluded, SpotHealing.Compute(excluded, layer.Id, mask));
        var session = new EditorSession(doc);session.Apply(_ => result);session.Undo();Assert.Same(doc, session.Document);session.Redo();Assert.Same(result, session.Document);Assert.Equal(bytes, layer.Pixels.ToRgba());
        string root = Path.Combine(Path.GetTempPath(), "Compositor-heal-" + Guid.NewGuid().ToString("N"));
        try { ProjectStore.Save(result, layer.Id, root + ".comp");Assert.Equal(healed.Pixels.ToRgba(), ProjectStore.Load(root + ".comp").Document.Layers[0].Pixels.ToRgba()); }
        finally
        {
            string target = Path.GetFullPath(root + ".comp"), parent = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!target.StartsWith(parent, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(target).StartsWith("Compositor-heal-")) throw new IOException("Unsafe healing cleanup.");
            if (Directory.Exists(target)) Directory.Delete(target, true);
        }
    }

    [Theory]
    [InlineData(HealingMode.ContentAware)]
    [InlineData(HealingMode.CreateTexture)]
    [InlineData(HealingMode.ProximityMatch)]
    public void ConstantSurroundingToneRepairsSpotAndPreservesOtherPixels(HealingMode mode)
    {
        int width = 513, height = 17, index = 8 * width + 255;
        var bytes = Enumerable.Repeat(new byte[] { 80, 120, 160, 255 }, width * height).SelectMany(p => p).ToArray();
        bytes[index * 4] = bytes[index * 4 + 1] = bytes[index * 4 + 2] = 0;
        var source = Raster.FromRgba(width, height, bytes);var mask = new byte[width * height];mask[index] = 255;
        var result = SpotHealing.Apply(source, mask, mode: mode, seed: 123);var output = result.ToRgba();
        double grain = 0;
        if (mode == HealingMode.CreateTexture)
        {
            static uint Hash(uint x) { unchecked { x ^= x >> 16;x *= 0x7feb352d;x ^= x >> 15;x *= 0x846ca68b;x ^= x >> 16;return x; } }
            // Cropped search region is [149,362), 213 pixels wide. A 5x5 ring
            // has 24 pixels; four ring pixels each neighbour the one black hole.
            uint key = Hash(123 ^ Hash(8 * 213 + 106));
            double u1 = (Hash(key) >> 8) / 16777216d, u2 = (Hash(key ^ 0x68e31da4) >> 8) / 16777216d;
            grain = Math.Sqrt(-2 * Math.Log(1 - u1)) * Math.Cos(2 * Math.PI * u2);
        }
        for (int c = 0; c < 4; c++)
        {
            int tone = new byte[] { 80, 120, 160, 255 }[c];
            double detail = c == 3 ? 0 : .9 * Math.Sqrt(4 * Math.Pow(tone / 4d, 2) / 24);
            int expected = (int)Math.Round(tone + grain * detail, MidpointRounding.AwayFromZero);
            Assert.InRange(Math.Abs(output[index * 4 + c] - expected), 0, 1);
        }
        for (int p = 0; p < bytes.Length; p++) if (p / 4 != index) Assert.Equal(bytes[p], output[p]);
        Assert.Equal(bytes, source.ToRgba());Assert.Same(source.Tiles[new(2, 0)], result.Tiles[new(2, 0)]);
        Assert.Equal(output, SpotHealing.Apply(source, mask, mode: mode, seed: 123).ToRgba());
    }

    [Fact]
    public void CoverageAndOpacityApplyOnceToPremultipliedPixels()
    {
        var bytes = Enumerable.Repeat(new byte[] { 40, 60, 80, 128 }, 32 * 32).SelectMany(p => p).ToArray();int index = 16 * 32 + 16;
        bytes[index * 4] = bytes[index * 4 + 1] = bytes[index * 4 + 2] = 0;
        var source = Raster.FromRgba(32, 32, bytes);var mask = new byte[1024];mask[index] = 128;
        var output = SpotHealing.Apply(source, mask, .5).ToRgba();
        Assert.Equal(new byte[] { 10, 15, 20, 128 }, output.Skip(index * 4).Take(4).ToArray());
        Assert.Same(source, SpotHealing.Apply(source, mask, 0));Assert.Same(source, SpotHealing.Apply(source, new byte[1024]));
        Assert.Throws<ArgumentException>(() => SpotHealing.Apply(source, mask, mode: (HealingMode)99));
        Assert.Throws<OperationCanceledException>(() => SpotHealing.Apply(source, mask, cancellation: new CancellationToken(true)));
    }
}
