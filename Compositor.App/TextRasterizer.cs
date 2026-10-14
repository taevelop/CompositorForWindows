using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
using Alignment = System.Windows.TextAlignment;

namespace Compositor.App;

internal sealed record TextRasterResult(Raster Pixels, string RequestedFont, string ResolvedFamily, bool MissingFont)
{ internal string ResolvedFace { get; init; } = ""; }

/// <summary>WPF shaping/fallback and word layout, producing UI-independent premultiplied RGBA pixels.</summary>
internal static class TextRasterizer
{
    internal static TextRasterResult Render(LayerTextStyle style, CancellationToken cancellation = default)
    {
        style.Validate();cancellation.ThrowIfCancellationRequested();
        if (style.Tracking != 0) throw new NotSupportedException("Tracked text rendering is not connected yet. Existing text pixels remain unchanged.");
        var resolved = TextFontResolver.Resolve(style.FontName);var family = resolved.Typeface.FontFamily;bool missing = resolved.MissingFont;
        var brush = new SolidColorBrush(Color.FromRgb((byte)Math.Round(style.Red * 255), (byte)Math.Round(style.Green * 255), (byte)Math.Round(style.Blue * 255)));brush.Freeze();
        var text = new FormattedText(style.Content, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, resolved.Typeface, style.FontSize, brush, 1)
        {
            LineHeight = style.LineHeight,
            TextAlignment = style.Alignment switch { Compositor.Core.TextAlignment.Center => Alignment.Center, Compositor.Core.TextAlignment.Right => Alignment.Right, _ => Alignment.Left },
            Trimming = TextTrimming.None
        };
        double padding = LayerTextStyle.Padding;int width, height;
        if (style.BoxSize is { } box)
        {
            width = checked((int)Math.Ceiling(box.Width));height = checked((int)Math.Ceiling(box.Height));text.MaxTextWidth = Math.Max(1, width - padding * 2);
        }
        else
        {
            text.MaxTextWidth = 100_000;
            width = checked((int)Math.Max(16, Math.Ceiling(text.WidthIncludingTrailingWhitespace + padding * 2 + style.FontSize * .1)));
            height = checked((int)Math.Max(16, Math.Ceiling(Math.Max(text.Height, Math.Ceiling(style.LineHeight)) + padding * 2)));
            text.MaxTextWidth = Math.Max(1, width - padding * 2);
        }
        Limits.CheckDimensions(width, height);cancellation.ThrowIfCancellationRequested();
        var visual = new DrawingVisual();RenderOptions.SetClearTypeHint(visual, ClearTypeHint.Auto);
        using (var context = visual.RenderOpen())
        {
            context.PushClip(new RectangleGeometry(new Rect(padding, padding, Math.Max(1, width - padding * 2), Math.Max(1, height - padding * 2))));
            context.DrawText(text, new Point(padding, padding));context.Pop();
        }
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);bitmap.Render(visual);
        cancellation.ThrowIfCancellationRequested();var bytes = new byte[checked(width * height * 4)];bitmap.CopyPixels(bytes, width * 4, 0);
        for (int y = 0; y < height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++) { int p = (y * width + x) * 4;(bytes[p], bytes[p + 2]) = (bytes[p + 2], bytes[p]); }
        }
        return new(Raster.FromRgba(width, height, bytes, cancellation), style.FontName, family.Source, missing) { ResolvedFace = resolved.Typeface.ToString() ?? family.Source };
    }
}
