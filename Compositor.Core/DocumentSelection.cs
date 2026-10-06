namespace Compositor.Core;

public enum SelectionMode { Replace, Add, Subtract }
/// <summary>Immutable document-space SVG outline. Null selection is unrestricted; empty outline selects nothing.</summary>
public sealed record DocumentSelection(string PathData, bool Antialiased = true, double Feather = 0, bool EvenOdd = false)
{
    public static DocumentSelection Empty { get; } = new("");
    public bool IsEmpty => PathData.Length == 0;
    public void Validate()
    {
        if (PathData is null || PathData.Length > 1_000_000 || !double.IsFinite(Feather) || Feather is < 0 or > 250)
            throw new InvalidDataException("Invalid selection outline or feather.");
    }
}
