using System.Runtime.InteropServices;
using System.Windows;
using Compositor.Core;

namespace Compositor.App;

internal sealed record TextRasterResult(Raster Pixels, string RequestedFont, string ResolvedFamily, bool MissingFont)
{ internal string ResolvedFace { get; init; } = ""; }

/// <summary>DirectWrite shaping/spacing and Direct2D color glyphs into premultiplied RGBA pixels.</summary>
internal static class TextRasterizer
{
    [DllImport("Compositor.Native.dll", CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int compositor_text_raster(string content, uint length, string family, uint weight, uint fontStyle, uint stretch,
        float size, float tracking, float leading, uint alignment, float red, float green, float blue, uint boxWidth, uint boxHeight,
        out uint width, out uint height, [Out] byte[]? output, uint outputLength);

    internal static TextRasterResult Render(LayerTextStyle style, CancellationToken cancellation = default)
    {
        style.Validate(); cancellation.ThrowIfCancellationRequested();
        var resolved = TextFontResolver.Resolve(style.FontName); var face = resolved.Typeface; var family = face.FontFamily.Source;
        uint fontStyle = face.Style == FontStyles.Italic ? 2u : face.Style == FontStyles.Oblique ? 1u : 0u;
        uint boxWidth = style.BoxSize is { } box ? checked((uint)Math.Ceiling(box.Width)) : 0;
        uint boxHeight = style.BoxSize is { } bounds ? checked((uint)Math.Ceiling(bounds.Height)) : 0;
        int Draw(byte[]? bytes, out uint width, out uint height) => compositor_text_raster(style.Content, (uint)style.Content.Length, family,
            (uint)face.Weight.ToOpenTypeWeight(), fontStyle, (uint)face.Stretch.ToOpenTypeStretch(), (float)style.FontSize,
            (float)style.Tracking, (float)style.LineHeight, (uint)style.Alignment, (float)style.Red, (float)style.Green, (float)style.Blue,
            boxWidth, boxHeight, out width, out height, bytes, (uint)(bytes?.Length ?? 0));
        cancellation.ThrowIfCancellationRequested();
        Marshal.ThrowExceptionForHR(Draw(null, out uint width, out uint height));
        Limits.CheckDimensions(checked((int)width), checked((int)height)); cancellation.ThrowIfCancellationRequested();
        var bytes = new byte[checked((int)(width * height * 4))];
        Marshal.ThrowExceptionForHR(Draw(bytes, out uint renderedWidth, out uint renderedHeight));
        if (renderedWidth != width || renderedHeight != height) throw new InvalidOperationException("Text dimensions changed while rendering.");
        cancellation.ThrowIfCancellationRequested();
        for (int y = 0; y < height; y++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < width; x++) { int p = checked((int)((y * width + x) * 4)); (bytes[p], bytes[p + 2]) = (bytes[p + 2], bytes[p]); }
        }
        return new(Raster.FromRgba((int)width, (int)height, bytes, cancellation), style.FontName, family, resolved.MissingFont)
        { ResolvedFace = face.ToString() ?? family };
    }
}