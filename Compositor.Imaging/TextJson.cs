using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;

namespace Compositor.Imaging;
internal static class TextJson
{
    internal static LayerTextStyle? Read(JsonElement layer)
    {
        if (!layer.TryGetProperty("text", out var node) || node.ValueKind == JsonValueKind.Null) return null;
        if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid text object.");
        string[] allowed = ["content", "fontName", "fontSize", "red", "green", "blue", "alignment", "tracking", "leading", "boxSize"];
        var seen = new HashSet<string>();
        foreach (var field in node.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new InvalidDataException("Duplicate text field.");
            if (!allowed.Contains(field.Name)) throw new NotSupportedException($"Unknown text field: {field.Name}.");
        }
        var alignment = node.GetProperty("alignment").GetString() switch
        { "Left" => TextAlignment.Left, "Center" => TextAlignment.Center, "Right" => TextAlignment.Right, _ => throw new NotSupportedException("Unknown text alignment.") };
        TextBoxSize? size = null;
        if (node.TryGetProperty("boxSize", out var box) && box.ValueKind != JsonValueKind.Null)
        {
            if (box.ValueKind != JsonValueKind.Array || box.GetArrayLength() != 2) throw new InvalidDataException("Invalid text box size.");
            size = new(box[0].GetDouble(), box[1].GetDouble());
        }
        double leading = node.TryGetProperty("leading", out var value) ? value.GetDouble() : 0;
        var result = new LayerTextStyle(node.GetProperty("content").GetString()!, node.GetProperty("fontName").GetString()!, node.GetProperty("fontSize").GetDouble(),
            node.GetProperty("red").GetDouble(), node.GetProperty("green").GetDouble(), node.GetProperty("blue").GetDouble(), alignment, node.GetProperty("tracking").GetDouble(), leading, size);
        result.Validate();return result;
    }
    internal static JsonObject Write(LayerTextStyle text)
    {
        text.Validate();var node = new JsonObject { ["content"] = text.Content, ["fontName"] = text.FontName, ["fontSize"] = text.FontSize,
            ["red"] = text.Red, ["green"] = text.Green, ["blue"] = text.Blue, ["alignment"] = text.Alignment.ToString(), ["tracking"] = text.Tracking, ["leading"] = text.Leading };
        if (text.BoxSize is { } box) node["boxSize"] = new JsonArray(box.Width, box.Height);
        return node;
    }
}
