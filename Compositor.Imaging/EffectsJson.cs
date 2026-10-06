using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;

namespace Compositor.Imaging;

internal static class EffectsJson
{
    public static LayerEffects? Read(JsonElement layer)
    {
        if (!layer.TryGetProperty("effects", out var node) || node.ValueKind == JsonValueKind.Null) return null;
        Fields(node, ["colorOverlay", "stroke", "shadow", "innerShadow", "outerGlow"]);
        foreach (var field in node.EnumerateObject())
            if (field.Name != "colorOverlay" && field.Value.ValueKind != JsonValueKind.Null)
                throw new NotSupportedException($"Unsupported layer effect: {field.Name}. Nothing was opened or changed.");
        if (!node.TryGetProperty("colorOverlay", out var overlay) || overlay.ValueKind == JsonValueKind.Null) return new();
        Fields(overlay, ["red", "green", "blue", "opacity", "enabled"]);
        bool? enabled = overlay.TryGetProperty("enabled", out var flag) && flag.ValueKind != JsonValueKind.Null ? flag.GetBoolean() : null;
        var result = new ColorOverlayEffect(overlay.GetProperty("red").GetDouble(), overlay.GetProperty("green").GetDouble(),
            overlay.GetProperty("blue").GetDouble(), overlay.GetProperty("opacity").GetDouble(), enabled);
        result.Validate(); return new(result);
    }
    private static void Fields(JsonElement node, string[] allowed)
    {
        if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid effects object.");
        var seen = new HashSet<string>();
        foreach (var field in node.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new InvalidDataException("Duplicate effects field.");
            if (!allowed.Contains(field.Name)) throw new NotSupportedException($"Unknown effects field: {field.Name}.");
        }
    }
    public static JsonObject Write(LayerEffects effects)
    {
        effects.Validate(); var result = new JsonObject();
        if (effects.ColorOverlay is { } overlay)
        {
            var value = new JsonObject { ["red"] = overlay.Red, ["green"] = overlay.Green, ["blue"] = overlay.Blue, ["opacity"] = overlay.Opacity };
            if (overlay.Enabled is { } enabled) value["enabled"] = enabled;
            result["colorOverlay"] = value;
        }
        return result;
    }
}
