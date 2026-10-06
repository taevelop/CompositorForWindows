using Compositor.Core;
using Compositor.Imaging;
using Xunit;
namespace Compositor.Tests;
public sealed class SelectionTests
{
    [Fact] public void NoneAndExplicitEmptyHaveDifferentMeaning()
    {
        var source=Raster.FromRgba(1,1,[10,20,30,255]);var changed=Raster.FromRgba(1,1,[90,80,70,255]);var t=new LayerTransform(0,0,1,1);
        Assert.Same(changed,SelectionPixels.Blend(source,changed,t,null));
        Assert.Same(source,SelectionPixels.Blend(source,changed,t,SelectionCoverage.Create(DocumentSelection.Empty,1,1)));
        Assert.Null(SelectionGeometry.Combine(null,SelectionGeometry.Box(0,0,1,1),SelectionMode.Subtract,1,1));
    }
    [Fact] public void RectangleIsClippedAndCoverageOnlyAllocatesItsRegion()
    {
        var selection=SelectionGeometry.Combine(null,SelectionGeometry.Box(-5,10,15,20),SelectionMode.Replace,4000,4000)!;
        var coverage=SelectionCoverage.Create(selection,4000,4000);
        Assert.True(coverage.Width<20&&coverage.Height<25);
        Assert.Equal(1,coverage.Sample(new(.5,10.5)));Assert.Equal(0,coverage.Sample(new(10.5,10.5)));
        Assert.Equal(0,coverage.Sample(new(-.1,20)));
    }
    [Fact] public void UnionDifferenceAndInverseKeepHolesAfterSvgRoundtrip()
    {
        var outer=SelectionGeometry.Box(2,2,12,12);
        var hollow=SelectionGeometry.Combine(outer,SelectionGeometry.Box(5,5,6,6),SelectionMode.Subtract,20,20)!;
        Assert.True(SelectionGeometry.Contains(hollow,new(3,3)));Assert.False(SelectionGeometry.Contains(hollow,new(7,7)));
        var inverse=SelectionGeometry.Invert(hollow,20,20);
        Assert.True(SelectionGeometry.Contains(inverse,new(7,7)));Assert.False(SelectionGeometry.Contains(inverse,new(3,3)));
        var restored=SelectionGeometry.Combine(hollow,SelectionGeometry.Box(5,5,6,6),SelectionMode.Add,20,20)!;
        Assert.True(SelectionGeometry.Contains(restored,new(7,7)));
    }
    [Fact] public void EllipseAndPolygonCoverageAreAntialiased()
    {
        var oval=SelectionGeometry.Box(1,1,16,10,ellipse:true);
        Assert.True(SelectionGeometry.Contains(oval,new(9,6)));Assert.False(SelectionGeometry.Contains(oval,new(1,1)));
        Assert.Contains(SelectionCoverage.Create(oval,20,20).Pixels.ToArray(),p=>p>0&&p<255);
        var triangle=SelectionGeometry.Polygon([new(1,1),new(15,1),new(1,15)]);
        Assert.True(SelectionGeometry.Contains(triangle,new(3,3)));Assert.False(SelectionGeometry.Contains(triangle,new(14,14)));
    }
    [Fact] public void ExpandContractMoveAndFeatherFollowOriginalRules()
    {
        var box=SelectionGeometry.Box(10,10,10,10);
        var expanded=SelectionGeometry.Resize(box,3,30,30);
        Assert.True(SelectionGeometry.Contains(expanded,new(8,15)));Assert.False(SelectionGeometry.Contains(expanded,new(6,15)));
        Assert.True(SelectionGeometry.Resize(box,-6,30,30).IsEmpty);
        var moved=SelectionGeometry.Move(box,5,-5);Assert.True(SelectionGeometry.Contains(moved,new(17,7)));Assert.False(SelectionGeometry.Contains(moved,new(12,12)));
        var feather=SelectionGeometry.Feather(SelectionGeometry.Feather(box,3),4);
        Assert.Equal(5,feather.Feather);
        var coverage=SelectionCoverage.Create(feather,30,30);
        Assert.InRange(coverage.Sample(new(9.5,15)),.05,.49);
        Assert.InRange(coverage.Sample(new(10.5,15)),.5,.9);
    }
    [Fact] public void FullCanvasFeatherAndFractionalSamplingDoNotDarkenCanvasEdges()
    {
        var full=SelectionGeometry.Feather(SelectionGeometry.Box(0,0,8,8),4);var coverage=SelectionCoverage.Create(full,8,8);
        Assert.Equal(1,coverage.Sample(new(.1,.1)));Assert.Equal(1,coverage.Sample(new(7.9,7.9)));
        Assert.Equal(0,coverage.Sample(new(-.1,.1)));
    }
    [Fact] public void TransformedPixelCentersUseDocumentSpaceSelection()
    {
        var source=Raster.FromRgba(2,1,[20,30,40,128,20,30,40,128]);
        var edited=Raster.FromRgba(2,1,[80,90,100,128,80,90,100,128]);
        var transform=new LayerTransform(10,10,2,2,90);
        var point=transform.ToDocument(new(.5,.5),2,1);
        var coverage=SelectionCoverage.Create(SelectionGeometry.Box(point.X-.5,point.Y-.5,1,1,antialiased:false),30,30);
        var result=SelectionPixels.Blend(source,edited,transform,coverage).ToRgba();
        Assert.True(result[0]>20);Assert.Equal(20,result[4]);Assert.Equal(128,result[3]);Assert.Equal(128,result[7]);
        Assert.Equal(new byte[]{20,30,40,128,20,30,40,128},source.ToRgba());
    }
    [Fact] public void FeatherBlendsRgbaAndUnchangedTilesStayShared()
    {
        var bytes=new byte[512*4];for(int p=0;p<bytes.Length;p+=4){bytes[p]=40;bytes[p+3]=80;}
        var original=Raster.FromRgba(512,1,bytes);var cleared=new Raster(512,1);
        var coverage=SelectionCoverage.Create(SelectionGeometry.Box(300,0,40,1),512,1);
        var result=SelectionPixels.Blend(original,cleared,new(0,0,512,1),coverage);
        Assert.Same(original.Tiles[new(0,0)],result.Tiles[new(0,0)]);
        Assert.Equal(0,result.ToRgba()[310*4+3]);Assert.Equal(80,result.ToRgba()[280*4+3]);
    }
    [Fact] public void SelectionUndoRedoAndCancelLeavePixelsShared()
    {
        var original=Document.Create(20,20);var session=new EditorSession(original);
        var selected=original with{Selection=SelectionGeometry.Box(2,2,5,5)};
        session.Apply(_=>selected);Assert.True(session.CanUndo);Assert.Same(original.Layers[0].Pixels,session.Document.Layers[0].Pixels);
        session.Undo();Assert.Null(session.Document.Selection);session.Redo();Assert.Same(selected,session.Document);
        session.Begin();session.Preview(selected with{Selection=DocumentSelection.Empty});session.Cancel();Assert.Same(selected,session.Document);
    }
    [Theory][InlineData(-1)][InlineData(251)][InlineData(double.NaN)]
    public void InvalidFeatherIsRejected(double value)=>Assert.Throws<InvalidDataException>(()=>new DocumentSelection("",Feather:value).Validate());
    [Fact] public void InvalidGeometryIsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(()=>SelectionGeometry.Box(double.NaN,0,1,1));
        Assert.Throws<ArgumentOutOfRangeException>(()=>SelectionGeometry.Resize(DocumentSelection.Empty,501,10,10));
        Assert.Throws<InvalidDataException>(()=>SelectionGeometry.Path(new("not an SVG path")));
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void SelectedStrokeMatchesSingleBlendAndDoesNotAccumulateFeather(bool erase)
    {
        var source=LayerMask.Solid(520,30,100).Pixels;
        var layer=Layer.Blank("Stroke",520,30) with { Pixels=source };
        var coverage=SelectionCoverage.Create(SelectionGeometry.Feather(SelectionGeometry.Box(240,0,40,30),4),520,30);
        var settings=new BrushSettings(30,1,.5,240,10,20,erase);
        var selected=new BrushStroke(layer,settings,520,30,coverage);
        var raw=new BrushStroke(layer,settings,520,30);
        selected.Append(new(220,15));raw.Append(new(220,15));
        var snapshot=selected.Pixels;var snapshotBytes=snapshot.ToRgba();
        for(int x=221;x<=300;x++){selected.Append(new(x,15));raw.Append(new(x,15));}
        Assert.Equal(SelectionPixels.Blend(source,raw.Pixels,layer.Transform,coverage).ToRgba(),selected.Pixels.ToRgba());
        Assert.Equal(snapshotBytes,snapshot.ToRgba());
        var completed=selected.Pixels;selected.Append(new(300,15));Assert.Same(completed,selected.Pixels);
        Assert.Equal(source.ToRgba().AsSpan(0,4).ToArray(),selected.Pixels.ToRgba().AsSpan(0,4).ToArray());
    }
    [Fact] public void EmptySelectionDoesNotAllocateStrokeTilesOrModifyMask()
    {
        var layer=Layer.Blank("Paint",600,300) with { Mask=LayerMask.Solid(600,300) };
        var coverage=SelectionCoverage.Create(DocumentSelection.Empty,600,300);
        var brush=new BrushStroke(layer,new(800,1,1,200,0,0),600,300,coverage);brush.Append(new(300,150));
        Assert.Same(layer.Pixels,brush.Pixels);
        var mask=new MaskStroke(layer,new(800,1,1,0,0,0),600,300,coverage);mask.Append(new(300,150));
        Assert.Same(layer.Mask,mask.Mask);
    }
    [Fact] public void SelectedMaskStrokeUsesLayerTransformAndKeepsOpaqueGray()
    {
        var layer=Layer.Blank("Mask",8,8) with { Transform=new(10,10,8,8,90),Mask=LayerMask.Solid(8,8) };
        var point=layer.Transform.ToDocument(new(2.5,3.5),8,8);
        var coverage=SelectionCoverage.Create(SelectionGeometry.Box(point.X-.5,point.Y-.5,1,1,false,false),30,30);
        var mask=new MaskStroke(layer,new(100,1,1,0,0,0),30,30,coverage);mask.Append(point);
        var result=mask.Mask.Pixels.ToRgba();
        Assert.Equal(new byte[]{0,0,0,255},result.AsSpan((3*8+2)*4,4).ToArray());
        Assert.Equal(new byte[]{255,255,255,255},result.AsSpan(0,4).ToArray());
        Assert.Equal(255,layer.Mask.Pixels.ToRgba()[(3*8+2)*4]);
    }    [Fact] public void SelectionIsTransientWhilePaintedPixelsRoundtrip()
    {
        string root=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"compositor-selection-"+Guid.NewGuid().ToString("N")+".comp");
        try
        {
            var doc=Document.Create(16,16) with { Selection=SelectionGeometry.Box(0,0,8,16) };
            var coverage=SelectionCoverage.Create(doc.Selection!,16,16);
            var stroke=new BrushStroke(doc.Layers[0],new(40,1,1,180,90,20),16,16,coverage);stroke.Append(new(8,8));
            doc=doc.Replace(doc.Layers[0] with{Pixels=stroke.Pixels});
            ProjectStore.Save(doc,null,root);var loaded=ProjectStore.Load(root).Document;
            Assert.Null(loaded.Selection);Assert.Equal(doc.Layers[0].Pixels.ToRgba(),loaded.Layers[0].Pixels.ToRgba());
        }
        finally
        {
            string temp=System.IO.Path.GetFullPath(System.IO.Path.GetTempPath());
            string full=System.IO.Path.GetFullPath(root);
            if(full.StartsWith(temp,StringComparison.OrdinalIgnoreCase)&&System.IO.Path.GetFileName(full).StartsWith("compositor-selection-")&&Directory.Exists(full))Directory.Delete(full,true);
        }
    }}
