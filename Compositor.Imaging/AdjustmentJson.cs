using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;

namespace Compositor.Imaging;

internal static class AdjustmentJson
{
    internal sealed record Parsed(ExposureAdjustment? Exposure, LevelsAdjustment? Levels, CurvesAdjustment? Curves = null, bool Invert = false, BlackWhiteAdjustment? BlackWhite = null, ColorBalanceAdjustment? ColorBalance = null, GrainAdjustment? Grain = null, GradientMapAdjustment? GradientMap = null);
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
        if (kind is not ("Exposure" or "Levels" or "Curves" or "Invert" or "Black & White" or "Color Balance" or "Grain" or "Gradient Map"))
            throw new NotSupportedException("This adjustment kind is not supported. Nothing was opened or changed.");
        bool exposure = kind == "Exposure";
        var defaults = JsonNode.Parse(Defaults)!.AsObject(); var seen = new HashSet<string>();
        foreach (var field in node.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new InvalidDataException("Duplicate adjustment field.");
            if (field.Name == "kind" || (exposure && field.Name == "exposureSettings") || (kind == "Levels" && field.Name == "levels") || (kind == "Curves" && field.Name == "curves") || (kind == "Black & White" && field.Name == "blackWhiteSettings") || (kind == "Color Balance" && field.Name == "colorBalanceSettings") || (kind == "Grain" && field.Name == "grainSettings") || (kind == "Gradient Map" && field.Name == "gradientMapSettings")) continue;
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
        if (kind == "Gradient Map") return new(null,null,GradientMap:node.TryGetProperty("gradientMapSettings",out var gm)&&gm.ValueKind!=JsonValueKind.Null?ReadGradientMap(gm):new());
        if (kind == "Color Balance") return new(null,null,ColorBalance:node.TryGetProperty("colorBalanceSettings",out var cb)&&cb.ValueKind!=JsonValueKind.Null?ReadColorBalance(cb):new());
        if (kind == "Grain") return new(null,null,Grain:node.TryGetProperty("grainSettings",out var gr)&&gr.ValueKind!=JsonValueKind.Null?ReadGrain(gr):new());
        if (kind == "Invert") return new(null,null,Invert:true);
        if (kind == "Black & White") return new(null,null,BlackWhite: node.TryGetProperty("blackWhiteSettings",out var bw) && bw.ValueKind != JsonValueKind.Null ? ReadBlackWhite(bw) : new());
        if (kind == "Levels") return new(null, LevelsJson.Read(node.GetProperty("levels")));
        if (kind == "Curves") return new(null, null, CurvesJson.Read(node.GetProperty("curves")));
        return new(node.TryGetProperty("exposureSettings", out var settings) && settings.ValueKind != JsonValueKind.Null ? ReadExposure(settings) : new(), null);
    }
    private static GradientMapAdjustment ReadGradientMap(JsonElement s)
    {
        Fields(s, ["shadows", "highlights", "reversed"]);
        AdjustmentColor Color(JsonElement c)
        {
            Fields(c, ["red", "green", "blue"]);
            return new(c.GetProperty("red").GetDouble(), c.GetProperty("green").GetDouble(), c.GetProperty("blue").GetDouble());
        }
        var value = new GradientMapAdjustment { Shadows = Color(s.GetProperty("shadows")), Highlights = Color(s.GetProperty("highlights")), Reversed = s.GetProperty("reversed").GetBoolean() };
        value.Validate(); return value;
    }
    public static JsonObject Write(GradientMapAdjustment s)
    {
        s.Validate(); var result = JsonNode.Parse(Defaults)!.AsObject(); result["kind"] = "Gradient Map";
        JsonObject Color(AdjustmentColor c) => new() { ["red"] = c.Red, ["green"] = c.Green, ["blue"] = c.Blue };
        result["gradientMapSettings"] = new JsonObject { ["shadows"] = Color(s.Shadows), ["highlights"] = Color(s.Highlights), ["reversed"] = s.Reversed };
        return result;
    }
    private static void Fields(JsonElement node, string[] names)
    {
        if(node.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid adjustment settings.");
        foreach(var p in node.EnumerateObject())if(!names.Contains(p.Name))throw new NotSupportedException($"Unknown adjustment field: {p.Name}.");
    }
    private static ColorBalanceAdjustment ReadColorBalance(JsonElement s)
    {
        Fields(s,["shadowCyanRed","shadowMagentaGreen","shadowYellowBlue","midCyanRed","midMagentaGreen","midYellowBlue","highlightCyanRed","highlightMagentaGreen","highlightYellowBlue","preserveLuminosity"]);
        var value=new ColorBalanceAdjustment(s.GetProperty("shadowCyanRed").GetDouble(),s.GetProperty("shadowMagentaGreen").GetDouble(),s.GetProperty("shadowYellowBlue").GetDouble(),s.GetProperty("midCyanRed").GetDouble(),s.GetProperty("midMagentaGreen").GetDouble(),s.GetProperty("midYellowBlue").GetDouble(),s.GetProperty("highlightCyanRed").GetDouble(),s.GetProperty("highlightMagentaGreen").GetDouble(),s.GetProperty("highlightYellowBlue").GetDouble(),s.GetProperty("preserveLuminosity").GetBoolean());
        value.Validate();return value;
    }
    private static GrainAdjustment ReadGrain(JsonElement s)
    {
        Fields(s,["amount","size","roughness","seed"]);
        var value=new GrainAdjustment(s.GetProperty("amount").GetDouble(),s.GetProperty("size").GetDouble(),s.GetProperty("roughness").GetDouble(),s.GetProperty("seed").GetUInt32());
        value.Validate();return value;
    }
    public static JsonObject Write(ColorBalanceAdjustment s)
    {
        s.Validate();var result=JsonNode.Parse(Defaults)!.AsObject();result["kind"]="Color Balance";
        result["colorBalanceSettings"]=new JsonObject{["shadowCyanRed"]=s.ShadowCyanRed,["shadowMagentaGreen"]=s.ShadowMagentaGreen,["shadowYellowBlue"]=s.ShadowYellowBlue,["midCyanRed"]=s.MidCyanRed,["midMagentaGreen"]=s.MidMagentaGreen,["midYellowBlue"]=s.MidYellowBlue,["highlightCyanRed"]=s.HighlightCyanRed,["highlightMagentaGreen"]=s.HighlightMagentaGreen,["highlightYellowBlue"]=s.HighlightYellowBlue,["preserveLuminosity"]=s.PreserveLuminosity};
        return result;
    }
    public static JsonObject Write(GrainAdjustment s)
    {
        s.Validate();var result=JsonNode.Parse(Defaults)!.AsObject();result["kind"]="Grain";
        result["grainSettings"]=new JsonObject{["amount"]=s.Amount,["size"]=s.Size,["roughness"]=s.Roughness,["seed"]=s.Seed};
        return result;
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
