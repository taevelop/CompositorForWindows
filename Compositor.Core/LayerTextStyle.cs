namespace Compositor.Core;

public enum TextAlignment { Left, Center, Right }
public sealed record TextBoxSize(double Width, double Height);

/// <summary>Original .comp editable text metadata; its cached pixels are the compositor source.</summary>
public sealed record LayerTextStyle(string Content = "Text", string FontName = "Helvetica", double FontSize = 72,
    double Red = 0, double Green = 0, double Blue = 0, TextAlignment Alignment = TextAlignment.Left,
    double Tracking = 0, double Leading = 0, TextBoxSize? BoxSize = null)
{
    public const double Padding = 12;
    public double LineHeight => Leading > 0 ? Leading : FontSize * 1.2;
    public void Validate()
    {
        if (Content is null || Content.Length > 100_000 || FontName is null || !Enum.IsDefined(Alignment) ||
            !double.IsFinite(FontSize) || FontSize is < 1 or > 2000 || new[] { Red, Green, Blue }.Any(v => !double.IsFinite(v) || v is < 0 or > 1) ||
            !double.IsFinite(Tracking) || Tracking is < -100 or > 1000 || !double.IsFinite(Leading) || Leading is < 0 or > 5000)
            throw new InvalidDataException("Invalid text style.");
        if (BoxSize is { } box && (!double.IsFinite(box.Width) || !double.IsFinite(box.Height) || box.Width is < 16 or > 30_000 || box.Height is < 16 or > 30_000 || box.Width * box.Height > Limits.MaxPixels))
            throw new InvalidDataException("Invalid text box size.");
    }
}
