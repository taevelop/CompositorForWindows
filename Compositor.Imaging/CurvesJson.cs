using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;

namespace Compositor.Imaging;

internal static class CurvesJson
{
    public static CurvesAdjustment Read(JsonElement node)
    {
        Fields(node, ["channel", "channels"]);
        string? name = node.GetProperty("channel").GetString();
        if (!Enum.TryParse<LevelsChannel>(name, out var channel) || !Enum.IsDefined(channel) || channel.ToString() != name)
            throw new NotSupportedException("Unknown Curves channel.");
        var channels = node.GetProperty("channels");
        if (channels.ValueKind != JsonValueKind.Array || channels.GetArrayLength() != 4) throw new InvalidDataException("Curves requires four channels.");
        var result = new CurvesAdjustment { Channel = channel }; int index = 0;
        foreach (var curve in channels.EnumerateArray())
        {
            if (curve.ValueKind != JsonValueKind.Array || curve.GetArrayLength() is < 2 or > 32) throw new InvalidDataException("A curve needs 2 to 32 points.");
            var points = new List<CurvePoint>();
            foreach (var point in curve.EnumerateArray()) { Fields(point, ["x", "y"]); points.Add(new(point.GetProperty("x").GetDouble(), point.GetProperty("y").GetDouble())); }
            result = result.WithCurve((LevelsChannel)index++, new(points.ToArray()));
        }
        result.Validate(); return result;
    }
    private static void Fields(JsonElement node, string[] allowed)
    {
        if (node.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid Curves object.");
        var seen = new HashSet<string>();
        foreach (var field in node.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new InvalidDataException("Duplicate Curves field.");
            if (!allowed.Contains(field.Name)) throw new NotSupportedException($"Unknown Curves field: {field.Name}.");
        }
    }
    public static JsonObject Write(CurvesAdjustment settings)
    {
        settings.Validate(); var channels = new JsonArray();
        foreach (var channel in Enum.GetValues<LevelsChannel>())
        {
            var points = new JsonArray(); foreach (var p in settings.Curve(channel).Points) points.Add(new JsonObject { ["x"] = p.X, ["y"] = p.Y });
            channels.Add(points);
        }
        return new() { ["channel"] = settings.Channel.ToString(), ["channels"] = channels };
    }
}
