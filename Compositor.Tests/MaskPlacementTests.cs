using Compositor.Core;
using Compositor.Imaging;
using Xunit;
using SkiaSharp;
using System.Text.Json.Nodes;
namespace Compositor.Tests;
public sealed class MaskPlacementTests
{
    [Fact] public void InvalidPlacementIsRejectedBeforeDecodingAndEnabledRoundTripMatchesOutput()
    {
        string root=Path.Combine(Path.GetTempPath(),"Compositor-placed-roundtrip-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            var d=Document.Create(8,8);var layer=d.Layers[0] with{Pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,1,0,0),8,8),
                Mask=LayerMask.Solid(2,2,128) with{Placement=new(1,2,4,5,17),Linked=false}};
            d=d.Replace(layer);ProjectStore.Save(d,layer.Id,root);var loaded=ProjectStore.Load(root).Document;
            using var a=new CanvasRenderer();using var b=new CanvasRenderer();Assert.Equal(Render(a,d),Render(b,loaded));
            string file=Path.Combine(root,"manifest.json");var json=JsonNode.Parse(File.ReadAllText(file))!;
            json["layers"]![0]!["maskPlacement"]!["size"]=new JsonArray(0,4);File.WriteAllText(file,json.ToJsonString());
            File.Delete(Path.Combine(root,"images",layer.Id.ToString().ToUpperInvariant()+".png"));
            Assert.Throws<InvalidDataException>(()=>ProjectStore.Load(root));
        }
        finally{if(Directory.Exists(root))Directory.Delete(root,true);File.Delete(root+".write-lock");}
    }
    [Fact] public void CanvasCropAndImageResizePreserveAndMoveIndependentMaskGrid()
    {
        var d=Document.Create(8,8);var mask=LayerMask.Solid(2,2) with{Placement=new(2,3,4,5),Linked=false};
        d=d.Replace(d.Layers[0] with{Mask=mask});
        var crop=CanvasCrop.Apply(d,new(1,2,4,4));
        Assert.Equal(mask.Placement! with{X=1,Y=1},crop.Layers[0].Mask!.Placement);
        Assert.Same(mask.Pixels,crop.Layers[0].Mask!.Pixels);
        var resized=ImageResize.Apply(d,new(16,16,72));
        Assert.Same(mask.Pixels,resized.Layers[0].Mask!.Pixels);
        Assert.Equal(new LayerTransform(4,6,8,10),resized.Layers[0].Mask!.Placement);
        Assert.Equal(new LayerTransform(2,3,4,5),d.Layers[0].Mask!.Placement);
    }
    [Fact] public void RotatedMaskResizePreservesPixelsLinkStateAndProjectOutput()
    {
        string path=Path.Combine(Path.GetTempPath(),"Compositor-resized-mask-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            var d=Document.Create(8,8);var placement=new LayerTransform(1,2,4,3,29,true,false);
            var mask=LayerMask.Solid(2,2,128) with{Placement=placement,Linked=false};
            d=d.Replace(d.Layers[0] with{Pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,.5,.5,.5),8,8),Mask=mask});
            var resized=ImageResize.Apply(d,new(16,24,72));var result=resized.Layers[0].Mask!;
            Assert.Same(mask.Pixels,result.Pixels);Assert.False(result.Linked);
            var oldCenter=placement.ToDocument(new(.5,.5),1,1);var center=result.Placement!.ToDocument(new(.5,.5),1,1);
            Assert.Equal(oldCenter.X*2,center.X,8);Assert.Equal(oldCenter.Y*3,center.Y,8);
            ProjectStore.Save(resized,null,path);var loaded=ProjectStore.Load(path).Document;
            Assert.Equal(result.Placement,loaded.Layers[0].Mask!.Placement);
            using var a=new CanvasRenderer();using var b=new CanvasRenderer();Assert.Equal(Render(a,resized),Render(b,loaded));
            using var cancelled=new CancellationTokenSource();cancelled.Cancel();
            Assert.Throws<OperationCanceledException>(()=>ImageResize.Apply(d,new(16,24,72),cancelled.Token));
            Assert.Same(mask.Pixels,d.Layers[0].Mask!.Pixels);
        }
        finally{if(Directory.Exists(path))Directory.Delete(path,true);File.Delete(path+".write-lock");}
    }
    [Fact] public void MaskBrushAndFillUseMaskPlacementAndKeepImageAndMetadata()
    {
        var d=Document.Create(16,16);var mask=LayerMask.Solid(8,8,0) with{Placement=new(4,4,8,8),Linked=false};
        var layer=d.Layers[0] with{Mask=mask};var settings=new BrushSettings(2,1,1,255,255,255);
        var actual=new MaskStroke(layer,settings,16,16);
        var expected=new MaskStroke(layer with{Transform=mask.Placement!},settings,16,16);
        actual.Append(new(5,5));expected.Append(new(5,5));
        Assert.Equal(expected.Mask.Pixels.ToRgba(),actual.Mask.Pixels.ToRgba());
        Assert.Equal(mask.Placement,actual.Mask.Placement);Assert.False(actual.Mask.Linked);
        var selection=SelectionGeometry.Box(4,4,2,2);
        var original=d.Replace(layer) with{Selection=selection};
        var filled=LayerFill.Apply(original,layer.Id,255,255,255,true);
        Assert.Same(layer.Pixels,filled.Layers[0].Pixels);Assert.Equal(mask.Placement,filled.Layers[0].Mask!.Placement);
        var bytes=filled.Layers[0].Mask!.Pixels.ToRgba();Assert.Equal(255,bytes[0]);Assert.Equal(0,bytes[7*4]);
    }
    [Fact] public void MaskOnlyTransformAndLinkedLayerTransformHaveOneReversibleEdit()
    {
        var d=Document.Create(8,8);var mask=LayerMask.Solid(8,8,128) with{Placement=new(1,2,8,8)};
        var layer=d.Layers[0] with{Mask=mask};d=d.Replace(layer);var session=new EditorSession(d){EditMask=true};
        using(var edit=LayerTransformEdit.Begin(session))
        {Assert.Equal(mask.Placement,edit.InitialTransform);edit.Preview(edit.InitialTransform with{X=4});edit.Complete();}
        Assert.Equal(layer.Transform,session.Document.Layers[0].Transform);Assert.Equal(4,session.Document.Layers[0].Mask!.Placement!.X);
        session.Undo();Assert.Same(d,session.Document);Assert.False(session.CanUndo);
        session.EditMask=false;
        using(var edit=LayerTransformEdit.Begin(session))
        {edit.Preview(layer.Transform with{X=3});edit.Complete();}
        Assert.Equal(4,session.Document.Layers[0].Mask!.Placement!.X);
        var unlinked=LayerPlacement.Change(layer with{Mask=mask with{Linked=false}},layer.Transform with{X=3});
        Assert.Equal(mask.Placement,unlinked.Mask!.Placement);
    }
    private static byte[] Render(CanvasRenderer renderer,Document document)
    {
        using var image=renderer.Flatten(document);using var bitmap=new SKBitmap(CanvasRenderer.Info(image.Width,image.Height));
        Assert.True(image.ReadPixels(bitmap.Info,bitmap.GetPixels(),bitmap.RowBytes,0,0));return bitmap.GetPixelSpan().ToArray();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void PlacedMaskScreenAndOutputMatchResolvedCoverageIncludingEffectsAndAdjustments(bool effects)
    {
        var d=Document.Create(8,8);var mask=LayerMask.Solid(2,2,128) with {Placement=new(1,2,3,4,17),Linked=false};
        var pixels=ShapeRaster.Create(new(ShapeKind.Rectangle,1,0,0),8,8);
        var layer=d.Layers[0] with{Pixels=pixels,Mask=mask};
        if(effects)layer=layer with{Effects=new(Stroke:new(1,0,0,1,1,false))};
        d=d.Replace(layer);d.Validate();
        var resolved=MaskPlacement.Resolve(mask,mask.Placement!,layer.Transform,8,8);
        var expected=d.Replace(layer with{Mask=resolved});
        using var actualRenderer=new CanvasRenderer();using var expectedRenderer=new CanvasRenderer();
        Assert.Equal(Render(expectedRenderer,expected),Render(actualRenderer,d));
        Assert.Equal(Render(expectedRenderer,expected),Render(actualRenderer,d));
        var adjusted=Layer.ExposureLayer(8,8) with{Mask=mask,Exposure=new(-1)};
        var actualAdjustment=d with{Layers=d.Layers.Add(adjusted)};
        var expectedAdjustment=expected with{Layers=expected.Layers.Add(adjusted with{Mask=resolved})};
        Assert.Equal(Render(expectedRenderer,expectedAdjustment),Render(actualRenderer,actualAdjustment));
        Assert.NotEqual(Render(actualRenderer,d),Render(actualRenderer,actualAdjustment));
        Assert.Same(mask.Pixels,d.Layers[0].Mask!.Pixels);Assert.Equal(mask.Placement,d.Layers[0].Mask!.Placement);
    }
    [Fact] public void PixelReplacementAndProjectRoundTripKeepPlacementAndLinkState()
    {
        var mask=LayerMask.Solid(2,2,128) with{Placement=new(2,3,4,5),Linked=false,Enabled=false};
        var changed=mask.WithPixels(LayerMask.Solid(2,2,255).Pixels);
        Assert.Equal(mask.Placement,changed.Placement);Assert.False(changed.Linked);Assert.False(changed.Enabled);
        string root=Path.Combine(Path.GetTempPath(),"Compositor-mask-placement-"+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            string path=Path.Combine(root,"Mask.comp");var d=Document.Create(8,8);ProjectStore.Save(d,null,path);
            var before=File.ReadAllBytes(Path.Combine(path,"manifest.json"));
            var placed=d.Replace(d.Layers[0] with{Mask=mask});ProjectStore.Save(placed,null,path);
            var loaded=ProjectStore.Load(path).Document.Layers[0].Mask!;
            Assert.Equal(mask.Placement,loaded.Placement);Assert.False(loaded.Linked);Assert.False(loaded.Enabled);
            Assert.Equal(mask.Pixels.ToRgba(),loaded.Pixels.ToRgba());
        }
        finally{Directory.Delete(root,true);}
    }
    [Fact] public void CacheReusesImmutableCoverageAndHonorsEnabledChangesCancellationAndLruBudget()
    {
        var mask=LayerMask.Solid(2,2,128);var t=new LayerTransform(0,0,2,2);
        var cache=new MaskPlacementCache(4L*PixelTile.ByteCount,2);
        var first=cache.Resolve(mask,t,t,2,2);
        var disabled=cache.Resolve(mask with{Enabled=false},t,t,2,2);
        Assert.Same(first.Pixels,disabled.Pixels);Assert.False(disabled.Enabled);Assert.Equal(1,cache.BuildCount);
        var second=cache.Resolve(mask,t with{X=1},t,2,2);
        cache.Resolve(mask,t,t,2,2); // touch first, so the shifted grid is evicted next.
        cache.Resolve(mask,t with{Y=1},t,2,2);
        Assert.Equal(2,cache.Count);Assert.True(cache.RetainedBytes<=4L*PixelTile.ByteCount);
        Assert.Same(first.Pixels,cache.Resolve(mask,t,t,2,2).Pixels);
        Assert.NotSame(second.Pixels,cache.Resolve(mask,t with{X=1},t,2,2).Pixels);
        using var cancel=new CancellationTokenSource();cancel.Cancel();int builds=cache.BuildCount;
        Assert.Throws<OperationCanceledException>(()=>cache.Resolve(mask,t with{X=1},t,2,2,cancel.Token));
        Assert.Equal(builds,cache.BuildCount);
        cache.Clear();Assert.Equal(0,cache.Count);Assert.Equal(0,cache.RetainedBytes);
        var tiny=new MaskPlacementCache(1);tiny.Resolve(mask,t,t,2,2);Assert.Equal(0,tiny.Count);
    }
    [Fact] public void RendererOwnsPlacementCacheAndSourcePixelReplacementInvalidatesIt()
    {
        using var renderer=new CanvasRenderer();var t=new LayerTransform(0,0,2,2);
        var mask=LayerMask.Solid(2,2,128);var first=renderer.ResolvePlacedMask(mask,t,t,2,2);
        Assert.Same(first.Pixels,renderer.ResolvePlacedMask(mask,t,t,2,2).Pixels);
        var changed=renderer.ResolvePlacedMask(LayerMask.Solid(2,2,255),t,t,2,2);
        Assert.NotSame(first.Pixels,changed.Pixels);Assert.Equal(255,changed.Pixels.ToRgba()[0]);
        Assert.Equal(128,first.Pixels.ToRgba()[0]);
    }
    [Fact] public void LinkedMovementFollowsLayerAndUnlinkedMovementKeepsDocumentPlacement()
    {
        var initial=new LayerTransform(10,20,8,8);var to=initial with{X=30,Width=16};
        Assert.Null(MaskPlacement.Move(null,true,initial,to));
        Assert.Equal(initial,MaskPlacement.Move(null,false,initial,to));
        var separate=initial with{X=12,Y=24,Width=4,Height=4};
        Assert.Equal(GroupTransform.Following(separate,initial,to),MaskPlacement.Move(separate,true,initial,to));
        Assert.Equal(separate,MaskPlacement.Move(separate,false,initial,to));
    }
    [Fact] public void ResolvePreservesSourceAndMapsRotationFlipAndDifferentGrids()
    {
        var bytes=new byte[]{0,0,0,255,255,255,255,255,0,0,0,255,255,255,255,255};
        var seed=LayerMask.Solid(2,2);var tile=seed.Pixels.Tiles[new(0,0)].Bytes.ToArray(); bytes.AsSpan(0,8).CopyTo(tile);bytes.AsSpan(8,8).CopyTo(tile.AsSpan(PixelTile.Stride));
        var mask=seed.WithPixels(new Raster(2,2,seed.Pixels.Tiles.SetItem(new(0,0),new PixelTile(tile))));
        var placement=new LayerTransform(0,0,2,2);
        Assert.Equal(bytes,MaskPlacement.Resolve(mask,placement,placement,2,2).Pixels.ToRgba());
        var flipped=MaskPlacement.Resolve(mask,placement with{FlipX=true},placement,2,2).Pixels.ToRgba();
        Assert.Equal(255,flipped[0]);Assert.Equal(0,flipped[4]);
        var rotated=MaskPlacement.Resolve(mask,placement with{Rotation=180},placement,2,2).Pixels.ToRgba();
        Assert.Equal(flipped,rotated);Assert.Equal(bytes,mask.Pixels.ToRgba());
        Assert.Equal(257,MaskPlacement.Resolve(mask,placement,new(0,0,2,2),257,3).Pixels.Width);
    }
    [Fact] public void BackgroundUsesMaskEdgesAndUniformMasksRemainShared()
    {
        var layer=new LayerTransform(0,0,4,4);var outside=layer with{X=20};
        foreach(byte gray in new byte[]{0,255})
        {
            var mask=LayerMask.Solid(2,2,gray);
            Assert.All(MaskPlacement.Resolve(mask,outside,layer,4,4).Pixels.ToRgba().Chunk(4),p=>Assert.Equal(gray,p[0]));
        }
        var uniform=LayerMask.Solid(1,1,128);Assert.Same(uniform,MaskPlacement.Resolve(uniform,outside,layer,4,4));
        using var cancel=new CancellationTokenSource();cancel.Cancel();
        Assert.Throws<OperationCanceledException>(()=>MaskPlacement.Resolve(uniform,outside,layer,4,4,cancel.Token));
    }
}
