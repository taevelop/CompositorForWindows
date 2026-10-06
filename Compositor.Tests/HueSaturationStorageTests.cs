using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Compositor.Core;
using Compositor.Imaging;
using SkiaSharp;
using Xunit;
namespace Compositor.Tests;
public sealed class HueSaturationStorageTests
{
    private const string Fixture="""
        {"adjustment":{"kind":"Hue/Saturation","hue":15,"saturation":-20,"lightness":3,"colorize":true,
        "hsvSettings":{"range":"Reds","colorize":false,"invertRange":true,
        "adjustments":["Blues",{"hue":-60,"saturation":-40,"lightness":10},"Reds",{"hue":20,"saturation":30,"lightness":0}],
        "bands":["Reds",{"falloffStart":310,"rangeStart":340,"rangeEnd":20,"falloffEnd":50}]}}}
        """;
    private static HueSaturationLayerAdjustment Parse(string json){using var d=JsonDocument.Parse(json);return AdjustmentJson.Read(d.RootElement)!.HueSaturation!;}
    private static byte[] Render(Document d){using var r=new CanvasRenderer();using var i=r.Flatten(d);using var b=new SKBitmap(CanvasRenderer.Info(i.Width,i.Height));Assert.True(i.ReadPixels(b.Info,b.GetPixels(),b.RowBytes,0,0));return b.GetPixelSpan().ToArray();}
    [Fact] public void SwiftPairArraysAndInactiveLegacyFieldsRoundtrip()
    {
        var s=Parse(Fixture);Assert.Equal(15,s.Hue);Assert.True(s.Colorize);Assert.False(s.Resolved.Colorize);Assert.Equal(2,s.Resolved.Adjustments.Count);
        Assert.Single(s.Resolved.Bands);Assert.Equal(HueBand.Default(HueRange.Greens),s.Resolved.Band(HueRange.Greens));
        var node=AdjustmentJson.Write(s);Assert.IsType<JsonArray>(node["hsvSettings"]!["adjustments"]);
        Assert.Equal(s,Parse(new JsonObject{["adjustment"]=node}.ToJsonString()));
    }
    [Fact] public void LegacyOnlyRemainsLegacyAndUsesMaster()
    {
        var s=Parse("""{"adjustment":{"kind":"Hue/Saturation","hue":120,"saturation":0,"lightness":0,"colorize":false}}""");
        Assert.Null(s.Settings);Assert.Equal(120,s.Resolved.Adjustment(HueRange.Master).Hue);
        Assert.False(AdjustmentJson.Write(s).ContainsKey("hsvSettings"));
        byte[] pixels=[255,0,0,255];HueSaturationProcessor.Apply(pixels,s.Resolved);Assert.Equal(new byte[]{0,255,0,255},pixels);
    }
    [Theory]
    [InlineData("\"Reds\",{\"hue\":20","\"Violet\",{\"hue\":20")]
    [InlineData("\"Reds\",{\"hue\":20","\"Blues\",{\"hue\":20")]
    [InlineData("\"falloffEnd\":50","\"falloffEnd\":50,\"future\":1")]
    [InlineData("\"hue\":20","\"hue\":361")]
    public void UnsupportedOrCorruptSettingsAreRejected(string old,string value)=>Assert.ThrowsAny<Exception>(()=>Parse(Fixture.Replace(old,value)));
    [Fact] public void ObjectDictionaryAndOddPairArrayAreRejected()
    {
        var node=JsonNode.Parse(Fixture)!;
        node["adjustment"]!["hsvSettings"]!["bands"]=new JsonObject();Assert.ThrowsAny<Exception>(()=>Parse(node.ToJsonString()));
        node["adjustment"]!["hsvSettings"]!["bands"]=new JsonArray("Reds");Assert.ThrowsAny<Exception>(()=>Parse(node.ToJsonString()));
    }
    [Fact] public void LayerCompositeMaskUndoAndProjectRoundtrip()
    {
        var doc=Document.Create(2,1);doc=doc.Replace(doc.Layers[0] with{Pixels=Raster.FromRgba(2,1,[255,0,0,255,0,0,128,128])});
        var a=Layer.HueSaturationLayer(2,1) with{HueSaturation=new(120,0,0)};
        var changed=doc with{Layers=doc.Layers.Add(a)};
        Assert.Equal(new byte[]{0,255,0,255,128,0,0,128},Render(changed));
        var masked=a with{Mask=LayerMask.Solid(2,1,0)};
        Assert.Equal(Render(doc),Render(doc with{Layers=doc.Layers.Add(masked)}));
        var stroke=new MaskStroke(masked,new(2,1,1,255,255,255),2,1);stroke.Append(new(1,.5));Assert.NotEqual(masked.Mask!.Pixels.ToRgba(),stroke.Mask.Pixels.ToRgba());
        var session=new EditorSession(doc);session.Apply(_=>changed);session.Undo();Assert.Same(doc,session.Document);session.Redo();Assert.Same(changed,session.Document);
        changed=changed.Replace(a with{HueSaturation=Parse(Fixture)});
        string root=Path.Combine(Path.GetTempPath(),"CompositorHSV-"+Guid.NewGuid().ToString("N")+".comp");
        try{ProjectStore.Save(changed,null,root);var loaded=ProjectStore.Load(root).Document;Assert.Equal(changed.Layers[1].HueSaturation,loaded.Layers[1].HueSaturation);Assert.Equal(Render(changed),Render(loaded));}
        finally{string full=Path.GetFullPath(root);if(!full.StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase)||!Path.GetFileName(full).StartsWith("CompositorHSV-"))throw new IOException("Unsafe cleanup.");if(Directory.Exists(full))Directory.Delete(full,true);}
    }
}
