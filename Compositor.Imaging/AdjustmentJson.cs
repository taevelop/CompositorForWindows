using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;

namespace Compositor.Imaging;

internal static class AdjustmentJson
{
    internal sealed record Parsed(ExposureAdjustment? Exposure, LevelsAdjustment? Levels, CurvesAdjustment? Curves = null, bool Invert = false, BlackWhiteAdjustment? BlackWhite = null);
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
        if (kind is not ("Exposure" or "Levels" or "Curves" or "Invert" or "Black & White"))
            throw new NotSupportedException("This adjustment kind is not supported. Nothing was opened or changed.");
        bool exposure = kind == "Exposure";
        var defaults = JsonNode.Parse(Defaults)!.AsObject(); var seen = new HashSet<string>();
        foreach (var field in node.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new InvalidDataException("Duplicate adjustment field.");
            if (field.Name == "kind" || (exposure && field.Name == "exposureSettings") || (kind == "Levels" && field.Name == "levels") || (kind == "Curves" && field.Name == "curves") || (kind == "Black & White" && field.Name == "blackWhiteSettings")) continue;
            if (field.Name == "exposureSettings")
            {
                if (field.Value.ValueKind != JsonValueKind.Null && !ReadExposure(field.Value).IsIdentity)
                    throw new NotSupportedException($"{kind} contains nondefault inactive Exposure settings.");
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
        if (kind == "Invert") return new(null,null,Invert:true);
        if (kind == "Black & White") return new(null,null,BlackWhite: node.TryGetProperty("blackWhiteSettings",out var bw) && bw.ValueKind != JsonValueKind.Null ? ReadBlackWhite(bw) : new());
        if (kind == "Levels") return new(null, LevelsJson.Read(node.GetProperty("levels")));
        if (kind == "Curves") return new(null, null, CurvesJson.Read(node.GetProperty("curves")));
        return new(node.TryGetProperty("exposureSettings", out var settings) && settings.ValueKind != JsonValueKind.Null ? ReadExposure(settings) : new(), null);
    }
    private static BlackWhiteAdjustment ReadBlackWhite(JsonElement s)
    {
        string[] names=["reds","yellows","greens","cyans","blues","magentas","tint","tintHue","tintSaturation"];
        if(s.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid Black & White settings.");
        foreach(var p in s.EnumerateObject())if(!names.Contains(p.Name))throw new NotSupportedException($"Unknown Black & White field: {p.Name}.");
        var value=new BlackWhiteAdjustment(s.GetProperty("reds").GetDouble(),s.GetProperty("yellows").GetDouble(),
            s.GetProperty("greens").GetDouble(),s.GetProperty("cyans").GetDouble(),s.GetProperty("blues").GetDouble(),s.GetProperty("magentas").GetDouble(),
            s.GetProperty("tint").GetBoolean(),s.GetProperty("tintHue").GetDouble(),s.GetProperty("tintSaturation").GetDouble());
        value.Validate();return value;
    }
    public static JsonObject WriteInvert() {var result=JsonNode.Parse(Defaults)!.AsObject();result["kind"]="Invert";return result;}
    public static JsonObject Write(BlackWhiteAdjustment s)
    {
        s.Validate();var result=JsonNode.Parse(Defaults)!.AsObject();result["kind"]="Black & White";
        result["blackWhiteSettings"]=new JsonObject{["reds"]=s.Reds,["yellows"]=s.Yellows,["greens"]=s.Greens,["cyans"]=s.Cyans,
            ["blues"]=s.Blues,["magentas"]=s.Magentas,["tint"]=s.Tint,["tintHue"]=s.TintHue,["tintSaturation"]=s.TintSaturation};
        return result;
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
    public static JsonObject Write(CurvesAdjustment curves)
    {
        curves.Validate(); var result = JsonNode.Parse(Defaults)!.AsObject();
        result["kind"] = "Curves"; result["curves"] = CurvesJson.Write(curves); return result;
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
