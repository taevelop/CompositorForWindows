using Compositor.Core;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Compositor.Imaging;
internal static class MaskPlacementJson
{
    public static LayerTransform? Read(JsonElement layer)
    {
        if(!layer.TryGetProperty("maskPlacement",out var t)||t.ValueKind==JsonValueKind.Null)return null;
        if(t.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid mask placement.");
        string[] allowed=["origin","size","rotation","flipX","flipY","sampling"];var seen=new HashSet<string>();
        foreach(var field in t.EnumerateObject())
        {
            if(!seen.Add(field.Name))throw new InvalidDataException("Duplicate mask placement field.");
            if(!allowed.Contains(field.Name))throw new NotSupportedException("Unknown mask placement field.");
        }
        var origin=Pair(t.GetProperty("origin"));var size=Pair(t.GetProperty("size"));
        var sampling=t.GetProperty("sampling").GetString() switch
        {"Nearest"=>Sampling.Nearest,"Smooth"=>Sampling.Smooth,"High quality"=>Sampling.High,_=>throw new NotSupportedException("Unknown mask sampling.")};
        var result=new LayerTransform(origin.X,origin.Y,size.X,size.Y,t.GetProperty("rotation").GetDouble(),
            t.GetProperty("flipX").GetBoolean(),t.GetProperty("flipY").GetBoolean(),sampling);
        result.Validate();return result;
    }
    private static PointD Pair(JsonElement p)
    {
        if(p.ValueKind!=JsonValueKind.Array||p.GetArrayLength()!=2)throw new InvalidDataException("Invalid mask point.");
        return new(p[0].GetDouble(),p[1].GetDouble());
    }
    public static JsonObject Write(LayerTransform t)=>new()
    {
        ["origin"]=new JsonArray(t.X,t.Y),["size"]=new JsonArray(t.Width,t.Height),["rotation"]=t.Rotation,
        ["flipX"]=t.FlipX,["flipY"]=t.FlipY,["sampling"]=t.Sampling==Sampling.High?"High quality":t.Sampling.ToString()
    };
}
