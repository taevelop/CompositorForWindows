using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;

namespace Compositor.Imaging;

internal static class ShapeJson
{
    public static LayerShapeStyle? Read(JsonElement layer)
    {
        if (!layer.TryGetProperty("shape", out var node) || node.ValueKind == JsonValueKind.Null) return null;
        if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid shape object.");
        string[] allowed = ["kind", "red", "green", "blue", "cornerRadius", "lineWidth", "start", "end"];
        var seen = new HashSet<string>();
        foreach (var field in node.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new InvalidDataException("Duplicate shape field.");
            if (!allowed.Contains(field.Name)) throw new NotSupportedException($"Unknown shape field: {field.Name}.");
        }
        var kind = node.GetProperty("kind").GetString() switch
        {
            "Rectangle" => ShapeKind.Rectangle, "Ellipse" => ShapeKind.Ellipse, "Line" => ShapeKind.Line,
            _ => throw new NotSupportedException("Unknown shape kind.")
        };
        double? width = node.TryGetProperty("lineWidth", out var w) && w.ValueKind != JsonValueKind.Null ? w.GetDouble() : null;
        var result = new LayerShapeStyle(kind, node.GetProperty("red").GetDouble(), node.GetProperty("green").GetDouble(),
            node.GetProperty("blue").GetDouble(), node.GetProperty("cornerRadius").GetDouble(), width, Point(node, "start"), Point(node, "end"));
        result.Validate(); return result;
    }
    private static PointD? Point(JsonElement node, string name)
    {
        if (!node.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null) return null;
        if (p.ValueKind != JsonValueKind.Array || p.GetArrayLength() != 2) throw new InvalidDataException("Invalid shape point.");
        return new(p[0].GetDouble(), p[1].GetDouble());
    }
    public static JsonObject Write(LayerShapeStyle style)
    {
        style.Validate();
        var node = new JsonObject { ["kind"] = style.Kind.ToString(), ["red"] = style.Red, ["green"] = style.Green,
            ["blue"] = style.Blue, ["cornerRadius"] = style.CornerRadius };
        if (style.LineWidth is {} width) node["lineWidth"] = width;
        if (style.Start is {} start) node["start"] = new JsonArray(start.X, start.Y);
        if (style.End is {} end) node["end"] = new JsonArray(end.X, end.Y);
        return node;
    }
}
