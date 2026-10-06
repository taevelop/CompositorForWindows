using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;

namespace Compositor.Tests;
public sealed class ShadowTests
{
    private static Document Sample(ShadowEffect? shadow = null)
    {
        var doc = Document.Create(600, 180);
        var bytes = new byte[300 * 60 * 4];
        for (int y = 10; y < 50; y++) for (int x = 10; x < 290; x++)
        { int p = (y * 300 + x) * 4; bytes[p] = 200; bytes[p + 1] = 80; bytes[p + 2] = 40; bytes[p + 3] = 255; }
        return doc.Replace(doc.Layers[0] with { Pixels = Raster.FromRgba(300, 60, bytes),
            Transform = new(80, 30, 300, 60), Effects = new(Shadow: shadow ?? new(90, 25, 0, Opacity: 1)) });
    }
    private static byte[] Pixels(SKImage image)
    {
        using var bitmap = new SKBitmap(CanvasRenderer.Info(image.Width, image.Height));
        Assert.True(image.ReadPixels(bitmap.Info, bitmap.GetPixels(), bitmap.RowBytes, 0, 0)); return bitmap.GetPixelSpan().ToArray();
    }
    private static byte[] Render(Document d) { using var renderer = new CanvasRenderer(); using var image = renderer.Flatten(d); return Pixels(image); }
    [Fact] public void ShadowExpandsBelowLayerAndLayerOpacityAppliesOnce()
    {
        var d = Sample(); byte[] actual = Render(d);
        Assert.Equal(255, actual[((95 * 600 + 120) * 4) + 3]);
        Assert.Equal(0, actual[((96 * 600 + 120) * 4)]);
        Assert.Equal(0, actual[((106 * 600 + 120) * 4) + 3]);
        var half = Render(d.Replace(d.Layers[0] with { Opacity = .5 }));
        Assert.InRange(half[(60 * 600 + 120) * 4 + 3], (byte)127, (byte)128);
        Assert.InRange(half[(95 * 600 + 120) * 4 + 3], (byte)127, (byte)128);
    }
    [Theory][InlineData(0)][InlineData(90)][InlineData(180)][InlineData(-90)]
    public void AngleFollowsLightDirection(double angle)
    {
        var s = new ShadowEffect(angle, 25, 0);
        Assert.Equal(-Math.Cos(angle * Math.PI / 180) * 25, s.OffsetX, 8);
        Assert.Equal(Math.Sin(angle * Math.PI / 180) * 25, s.OffsetY, 8);
        var d = Sample(s); var result = Render(d);
        int x = (int)Math.Round(80 + 150 + s.OffsetX), y = (int)Math.Round(30 + 30 + s.OffsetY);
        Assert.True(result[(y * 600 + x) * 4 + 3] > 0);
    }
    [Fact] public void MaskHidesShadowAndDisabledShadowEqualsPlainSource()
    {
        var d = Sample(); var l = d.Layers[0];
        Assert.All(Render(d.Replace(l with { Mask = LayerMask.Solid(1, 1, 0) })), b => Assert.Equal((byte)0, b));
        Assert.Equal(Render(d.Replace(l with { Effects = null })), Render(d.Replace(l with { Effects = new(Shadow: l.Effects!.Shadow! with { Enabled = false }) })));
        Assert.Equal(Render(d), Render(d.Replace(l with { Mask = LayerMask.Solid(1, 1, 0) with { Enabled = false } })));
    }
    [Fact] public void BlurHasNoTileSeamAndViewportInvalidatesAfterPainting()
    {
        var d = Sample(new(90, 25, 16)); var a = Render(d);
        // A long flat edge crosses x=256 in source coordinates.
        for (int y = 85; y < 120; y++) Assert.Equal(a[(y * 600 + 334) * 4 + 3], a[(y * 600 + 337) * 4 + 3]);
        using var viewport = new ViewportRenderer();
        using var before = viewport.Render(d, 720, 216, 1.2f, 0, 0);
        var stroke = new BrushStroke(d.Layers[0], new(35, .5, 1, 0, 0, 0, Erase: true), d.Width, d.Height);
        stroke.Append(new(340, 65));
        var edited = d.Replace(d.Layers[0] with { Pixels = stroke.Pixels });
        using var after = viewport.Render(edited, 720, 216, 1.2f, 0, 0);
        using var fresh = new ViewportRenderer(); using var expected = fresh.Render(edited, 720, 216, 1.2f, 0, 0);
        Assert.Equal(Pixels(expected), Pixels(after)); Assert.NotEqual(Pixels(before), Pixels(after));
    }
    [Fact] public void OverlayChangesSourceButNotShadowShapeAndNoPixelsAreMutated()
    {
        var d = Sample(new(90, 25, 0, Opacity: 1)); var l = d.Layers[0]; byte[] original = l.Pixels.ToRgba();
        var plain = Render(d);
        var combined = Render(d.Replace(l with { Effects = l.Effects! with { ColorOverlay = new(0, 1, 0, 1) } }));
        Assert.Equal(plain[(95 * 600 + 120) * 4 + 3], combined[(95 * 600 + 120) * 4 + 3]);
        Assert.Equal(255, combined[(60 * 600 + 120) * 4 + 1]); Assert.Equal(original, l.Pixels.ToRgba());
    }
    [Fact] public void SaveRoundTripAndUndoKeepBothEffects()
    {
        var d = Sample(new(125, 42, 12, .12345, .3, .8, .75, false));
        d = d.Replace(d.Layers[0] with { Effects = d.Layers[0].Effects! with { ColorOverlay = new(.2, .8, .3, .7) } });
        string root = Path.Combine(Path.GetTempPath(), "CompositorShadow-" + Guid.NewGuid().ToString("N") + ".comp");
        try
        {
            ProjectStore.Save(d, d.Layers[0].Id, root); var loaded = ProjectStore.Load(root).Document;
            Assert.Equal(d.Layers[0].Effects, loaded.Layers[0].Effects); Assert.Equal(Render(d), Render(loaded));
            var session = new EditorSession(loaded);
            session.Apply(x => x.Replace(x.Layers[0] with { Effects = x.Layers[0].Effects! with { Shadow = new() } }));
            Assert.Equal(0, session.HistoryRetainedBytes); session.Undo(); Assert.Equal(d.Layers[0].Effects, session.ActiveLayer!.Effects);
            session.Redo(); Assert.Equal(new ShadowEffect(), session.ActiveLayer!.Effects!.Shadow);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact] public void TransformedMaskedShadowMatchesExportAndRefreshesAfterMaskEdits()
    {
        var d = Sample(new(135, 30, 12, .1, .2, .4, .6));
        var layer = d.Layers[0] with { Mask = LayerMask.Solid(300, 60, 128),
            Transform = d.Layers[0].Transform with { Rotation = 27, FlipX = true },
            Effects = d.Layers[0].Effects! with { ColorOverlay = new(.8, .2, .1, .5) } };
        d = d.Replace(layer);
        using var viewport = new ViewportRenderer();
        using var initial = viewport.Render(d, 600, 180, 1, 0, 0);
        Assert.Equal(Render(d), Pixels(initial));
        var edited = d.Replace(layer with { Mask = LayerMask.Solid(300, 60, 0) });
        using var next = viewport.Render(edited, 600, 180, 1, 0, 0);
        Assert.Equal(Render(edited), Pixels(next));
        using var restored = viewport.Render(d, 600, 180, 1, 0, 0);
        Assert.Equal(Pixels(initial), Pixels(restored));
    }
    [Theory][InlineData(double.NaN,0,0)][InlineData(361,0,0)][InlineData(0,-1,0)][InlineData(0,5001,0)][InlineData(0,0,501)]
    public void InvalidValuesAreRejected(double angle, double distance, double blur) => Assert.Throws<InvalidDataException>(() => new ShadowEffect(angle,distance,blur).Validate());
}