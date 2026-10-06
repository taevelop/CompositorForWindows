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
            if (field.Name is not ("colorOverlay" or "shadow" or "stroke") && field.Value.ValueKind != JsonValueKind.Null)
                throw new NotSupportedException($"Unsupported layer effect: {field.Name}. Nothing was opened or changed.");
        StrokeEffect? stroke = null;
        if (node.TryGetProperty("stroke", out var st) && st.ValueKind != JsonValueKind.Null)
        {
            Fields(st, ["size", "red", "green", "blue", "opacity", "inside", "enabled"]);
            stroke = new(st.GetProperty("size").GetDouble(), st.GetProperty("red").GetDouble(), st.GetProperty("green").GetDouble(),
                st.GetProperty("blue").GetDouble(), st.GetProperty("opacity").GetDouble(), st.GetProperty("inside").GetBoolean(),
                st.TryGetProperty("enabled", out var flagStroke) && flagStroke.ValueKind != JsonValueKind.Null ? flagStroke.GetBoolean() : null);
            stroke.Validate();
        }
        ShadowEffect? shadow = null;
        if (node.TryGetProperty("shadow", out var s) && s.ValueKind != JsonValueKind.Null)
        {
            Fields(s, ["angle", "distance", "blur", "red", "green", "blue", "opacity", "enabled"]);
            shadow = new(s.GetProperty("angle").GetDouble(), s.GetProperty("distance").GetDouble(), s.GetProperty("blur").GetDouble(),
                s.GetProperty("red").GetDouble(), s.GetProperty("green").GetDouble(), s.GetProperty("blue").GetDouble(),
                s.GetProperty("opacity").GetDouble(), s.TryGetProperty("enabled", out var e) && e.ValueKind != JsonValueKind.Null ? e.GetBoolean() : null);
            shadow.Validate();
        }
        if (!node.TryGetProperty("colorOverlay", out var overlay) || overlay.ValueKind == JsonValueKind.Null) return new(Shadow: shadow, Stroke: stroke);
        Fields(overlay, ["red", "green", "blue", "opacity", "enabled"]);
        bool? enabled = overlay.TryGetProperty("enabled", out var flag) && flag.ValueKind != JsonValueKind.Null ? flag.GetBoolean() : null;
        var result = new ColorOverlayEffect(overlay.GetProperty("red").GetDouble(), overlay.GetProperty("green").GetDouble(),
            overlay.GetProperty("blue").GetDouble(), overlay.GetProperty("opacity").GetDouble(), enabled);
        result.Validate(); return new(result, shadow, stroke);
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
        if (effects.Shadow is { } shadow)
        {
            var value = new JsonObject { ["angle"] = shadow.Angle, ["distance"] = shadow.Distance, ["blur"] = shadow.Blur,
                ["red"] = shadow.Red, ["green"] = shadow.Green, ["blue"] = shadow.Blue, ["opacity"] = shadow.Opacity };
            if (shadow.Enabled is { } enabled) value["enabled"] = enabled;
            result["shadow"] = value;
        }
        if (effects.Stroke is { } stroke)
        {
            var value = new JsonObject { ["size"] = stroke.Size, ["red"] = stroke.Red, ["green"] = stroke.Green, ["blue"] = stroke.Blue,
                ["opacity"] = stroke.Opacity, ["inside"] = stroke.Inside };
            if (stroke.Enabled is { } enabled) value["enabled"] = enabled;
            result["stroke"] = value;
        }
        return result;
    }
}
