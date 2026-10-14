using System.IO;
using System.Windows;
using System.Windows.Media;
using Compositor.Imaging;

namespace Compositor.App;
internal sealed record TextFontResolution(Typeface Typeface, bool MissingFont);
internal static class TextFontResolver
{
    private static Dictionary<string, Typeface>? aliases;
    internal static TextFontResolution Resolve(string requested)
    {
        var family = Fonts.SystemFontFamilies.FirstOrDefault(f => f.Source.Equals(requested, StringComparison.OrdinalIgnoreCase) || f.FamilyNames.Values.Any(n => n.Equals(requested, StringComparison.OrdinalIgnoreCase)));
        if (family is not null) return new(new(family, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), false);
        aliases ??= BuildAliases();
        if (aliases.TryGetValue(requested, out var face)) return new(face, false);
        return new(new(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), true);
    }
    private static Dictionary<string, Typeface> BuildAliases()
    {
        var result = new Dictionary<string, Typeface>(StringComparer.OrdinalIgnoreCase);
        var files = new Dictionary<Uri, IReadOnlyList<OpenTypeFontFaceNames>>();
        foreach (var typeface in Fonts.SystemTypefaces)
        {
            try
            {
                if (!typeface.TryGetGlyphTypeface(out var glyph)) continue;
                if (!files.TryGetValue(glyph.FontUri, out var names))
                { using var stream = glyph.GetFontStream();files[glyph.FontUri] = names = OpenTypeFontNames.Read(stream); }
                foreach (var font in names)
                {
                    var families = font.Get(1).Concat(font.Get(16));var faces = font.Get(2).Concat(font.Get(17));
                    if (!families.Any(n => glyph.FamilyNames.Values.Concat(glyph.Win32FamilyNames.Values).Contains(n, StringComparer.OrdinalIgnoreCase)) ||
                        !faces.Any(n => glyph.FaceNames.Values.Concat(glyph.Win32FaceNames.Values).Contains(n, StringComparer.OrdinalIgnoreCase))) continue;
                    foreach (string alias in font.Get(4).Concat(font.Get(6))) result.TryAdd(alias, typeface);
                }
            }
            catch (Exception error) when (error is IOException or NotSupportedException or ArgumentException or InvalidOperationException or FileFormatException) { }
        }
        return result;
    }
}
