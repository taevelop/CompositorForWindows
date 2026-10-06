using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;
namespace Compositor.Imaging;

internal static class HueSaturationJson
{
    private static void Fields(JsonElement node, params string[] allowed)
    {
        if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected Hue/Saturation settings object.");
        var seen = new HashSet<string>();
        foreach (var field in node.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new InvalidDataException("Duplicate Hue/Saturation field.");
            if (!allowed.Contains(field.Name)) throw new NotSupportedException($"Unknown Hue/Saturation field: {field.Name}.");
        }
    }
    private static HueRange Range(JsonElement node)
    {
        string? name = node.GetString();
        if (!Enum.TryParse<HueRange>(name, out var range) || !Enum.IsDefined(range) || range.ToString() != name)
            throw new NotSupportedException("Unknown hue range.");
        return range;
    }
    // Swift enum-keyed dictionaries use alternating key/value arrays, not JSON objects.
    private static ImmutableDictionary<HueRange, T> Map<T>(JsonElement node, Func<JsonElement, T> read)
    {
        if (node.ValueKind != JsonValueKind.Array || node.GetArrayLength() % 2 != 0 || node.GetArrayLength() > 14)
            throw new InvalidDataException("Invalid Swift hue dictionary.");
        var result = ImmutableDictionary.CreateBuilder<HueRange, T>();
        for (int i = 0; i < node.GetArrayLength(); i += 2)
        {
            var key = Range(node[i]);
            if (result.ContainsKey(key)) throw new InvalidDataException("Duplicate hue range.");
            result.Add(key, read(node[i + 1]));
        }
        return result.ToImmutable();
    }
    private static HueRangeAdjustment Adjustment(JsonElement s)
    {
        Fields(s, "hue", "saturation", "lightness");
        return new(s.GetProperty("hue").GetDouble(), s.GetProperty("saturation").GetDouble(), s.GetProperty("lightness").GetDouble());
    }
    private static HueBand Band(JsonElement s)
    {
        Fields(s, "falloffStart", "rangeStart", "rangeEnd", "falloffEnd");
        return new(s.GetProperty("falloffStart").GetDouble(), s.GetProperty("rangeStart").GetDouble(), s.GetProperty("rangeEnd").GetDouble(), s.GetProperty("falloffEnd").GetDouble());
    }
    public static HueSaturationLayerAdjustment Read(JsonElement node)
    {
        HueSaturationAdjustment? settings = null;
        if (node.TryGetProperty("hsvSettings", out var s) && s.ValueKind != JsonValueKind.Null)
        {
            Fields(s, "range", "colorize", "invertRange", "adjustments", "bands");
            settings = new()
            {
                Range = Range(s.GetProperty("range")), Colorize = s.GetProperty("colorize").GetBoolean(),
                InvertRange = s.GetProperty("invertRange").GetBoolean(),
                Adjustments = Map(s.GetProperty("adjustments"), Adjustment), Bands = Map(s.GetProperty("bands"), Band)
            };
        }
        var result = new HueSaturationLayerAdjustment(node.GetProperty("hue").GetDouble(), node.GetProperty("saturation").GetDouble(),
            node.GetProperty("lightness").GetDouble(), node.GetProperty("colorize").GetBoolean(), settings);
        result.Validate(); return result;
    }
    public static void WriteTo(JsonObject node, HueSaturationLayerAdjustment value)
    {
        value.Validate();
        node["kind"] = "Hue/Saturation"; node["hue"] = value.Hue; node["saturation"] = value.Saturation;
        node["lightness"] = value.Lightness; node["colorize"] = value.Colorize;
        if (value.Settings is not { } s) return;
        var adjustments = new JsonArray(); var bands = new JsonArray();
        foreach (var (range, a) in s.Adjustments.OrderBy(p => p.Key))
        {
            adjustments.Add(range.ToString());
            adjustments.Add(new JsonObject { ["hue"] = a.Hue, ["saturation"] = a.Saturation, ["lightness"] = a.Lightness });
        }
        foreach (var (range, b) in s.Bands.OrderBy(p => p.Key))
        {
            bands.Add(range.ToString());
            bands.Add(new JsonObject { ["falloffStart"] = b.FalloffStart, ["rangeStart"] = b.RangeStart, ["rangeEnd"] = b.RangeEnd, ["falloffEnd"] = b.FalloffEnd });
        }
        node["hsvSettings"] = new JsonObject { ["range"] = s.Range.ToString(), ["colorize"] = s.Colorize,
            ["invertRange"] = s.InvertRange, ["adjustments"] = adjustments, ["bands"] = bands };
    }
}
