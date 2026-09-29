using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;

namespace Compositor.Imaging;

internal static class LevelsJson
{
    public static LevelsAdjustment Read(JsonElement value)
    {
        CheckFields(value, ["channel", "ranges"]);
        string name = value.GetProperty("channel").GetString()!;
        if (!Enum.TryParse<LevelsChannel>(name, out var channel) || !Enum.IsDefined(channel) || channel.ToString() != name)
            throw new NotSupportedException("Unknown Levels channel.");
        var ranges = value.GetProperty("ranges");
        if (ranges.ValueKind != JsonValueKind.Array || ranges.GetArrayLength() != 4) throw new InvalidDataException("Levels requires exactly four ranges.");
        var result = new LevelsAdjustment { Channel = channel }; int index = 0;
        foreach (var range in ranges.EnumerateArray())
        {
            CheckFields(range, ["black", "gamma", "white", "outputBlack", "outputWhite"]);
            result = result.WithRange((LevelsChannel)index++, new(range.GetProperty("black").GetDouble(), range.GetProperty("gamma").GetDouble(),
                range.GetProperty("white").GetDouble(), range.GetProperty("outputBlack").GetDouble(), range.GetProperty("outputWhite").GetDouble()));
        }
        result.Validate(); return result;
    }
    private static void CheckFields(JsonElement value, string[] allowed)
    {
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Invalid Levels object.");
        var seen = new HashSet<string>();
        foreach (var field in value.EnumerateObject())
        {
            if (!seen.Add(field.Name)) throw new InvalidDataException("Duplicate Levels field.");
            if (!allowed.Contains(field.Name)) throw new NotSupportedException($"Unknown Levels field: {field.Name}.");
        }
    }
    public static JsonObject Write(LevelsAdjustment settings)
    {
        settings.Validate(); var ranges = new JsonArray();
        foreach (var channel in Enum.GetValues<LevelsChannel>())
        {
            var r = settings.Range(channel); ranges.Add(new JsonObject { ["black"] = r.Black, ["gamma"] = r.Gamma, ["white"] = r.White, ["outputBlack"] = r.OutputBlack, ["outputWhite"] = r.OutputWhite });
        }
        return new() { ["channel"] = settings.Channel.ToString(), ["ranges"] = ranges };
    }
}
