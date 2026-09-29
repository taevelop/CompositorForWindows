using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;

namespace Compositor.Imaging;

internal static class AdjustmentJson
{
    internal sealed record Parsed(ExposureAdjustment? Exposure, LevelsAdjustment? Levels);
    // Swift synthesized Codable requires these nonoptional fields even for Exposure.
    private const string Defaults = """
        {"kind":"Exposure","hue":0,"saturation":0,"lightness":0,"colorize":false,
         "levels":{"channel":"RGB","ranges":[
            {"black":0,"gamma":1,"white":255,"outputBlack":0,"outputWhite":255},
            {"black":0,"gamma":1,"white":255,"outputBlack":0,"outputWhite":255},
            {"black":0,"gamma":1,"white":255,"outputBlack":0,"outputWhite":255},
            {"black":0,"gamma":1,"white":255,"outputBlack":0,"outputWhite":255}]},
         "curves":{"channel":"RGB","channels":[[{"x":0,"y":0},{"x":255,"y":255}],
            [{"x":0,"y":0},{"x":255,"y":255}],[{"x":0,"y":0},{"x":255,"y":255}],
            [{"x":0,"y":0},{"x":255,"y":255}]]}}
        """;
    public static Parsed? Read(JsonElement layer)
    {
        if (!layer.TryGetProperty("adjustment", out var node) || node.ValueKind == JsonValueKind.Null) return null;
        CheckDuplicates(node);
        if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid adjustment object.");
        string? kind = node.TryGetProperty("kind", out var kindNode) ? kindNode.GetString() : null;
        if (kind is not ("Exposure" or "Levels"))
            throw new NotSupportedException("Only Exposure and Levels adjustment layers are supported. Nothing was opened or changed.");
        bool exposure = kind == "Exposure";
        var defaults = JsonNode.Parse(Defaults)!.AsObject(); var seen = new HashSet<string>();
        foreach (var field in node.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new InvalidDataException("Duplicate adjustment field.");
            if (field.Name == "kind" || (exposure && field.Name == "exposureSettings") || (!exposure && field.Name == "levels")) continue;
            if (field.Name == "exposureSettings")
            {
                if (field.Value.ValueKind != JsonValueKind.Null && !ReadExposure(field.Value).IsIdentity)
                    throw new NotSupportedException("Levels contains nondefault inactive Exposure settings.");
                continue;
            }
            if (defaults.TryGetPropertyValue(field.Name, out var expected))
            {
                if (!JsonNode.DeepEquals(expected, JsonNode.Parse(field.Value.GetRawText())))
                    throw new NotSupportedException($"{kind} has unsupported nondefault {field.Name} settings.");
            }
            else if (field.Name is "hsvSettings" or "gradientMapSettings" or "grainSettings" or "blackWhiteSettings" or "colorBalanceSettings")
            {
                if (field.Value.ValueKind != JsonValueKind.Null) throw new NotSupportedException($"{kind} contains unsupported {field.Name} settings.");
            }
            else throw new NotSupportedException($"Unknown adjustment field: {field.Name}.");
        }
        if (!exposure) return new(null, LevelsJson.Read(node.GetProperty("levels")));
        return new(node.TryGetProperty("exposureSettings", out var settings) && settings.ValueKind != JsonValueKind.Null ? ReadExposure(settings) : new(), null);
    }
    private static ExposureAdjustment ReadExposure(JsonElement settings)
    {
        if (settings.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid exposure settings.");
        var seen = new HashSet<string>();
        foreach (var field in settings.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new InvalidDataException("Duplicate exposure field.");
            if (field.Name is not ("exposure" or "offset" or "gamma")) throw new NotSupportedException($"Unknown exposure field: {field.Name}.");
        }
        var result = new ExposureAdjustment(settings.GetProperty("exposure").GetDouble(), settings.GetProperty("offset").GetDouble(), settings.GetProperty("gamma").GetDouble());
        result.Validate(); return result;
    }
    private static void CheckDuplicates(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>();
            foreach (var field in value.EnumerateObject())
            {
                if (!names.Add(field.Name)) throw new InvalidDataException("Duplicate adjustment field.");
                CheckDuplicates(field.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array) foreach (var item in value.EnumerateArray()) CheckDuplicates(item);
    }
    public static JsonObject Write(LevelsAdjustment levels)
    {
        levels.Validate(); var result = JsonNode.Parse(Defaults)!.AsObject();
        result["kind"] = "Levels"; result["levels"] = LevelsJson.Write(levels); return result;
    }
    public static JsonObject Write(ExposureAdjustment exposure)
    {
        exposure.Validate(); var result = JsonNode.Parse(Defaults)!.AsObject();
        result["exposureSettings"] = new JsonObject { ["exposure"] = exposure.Exposure, ["offset"] = exposure.Offset, ["gamma"] = exposure.Gamma };
        return result;
    }
}
