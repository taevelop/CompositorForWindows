using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
using Compositor.Imaging;

namespace Compositor.App;
public partial class MainWindow
{
    private async Task SelectionSmokeTest(string path)
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        session.Load(Document.Create(64,64));Canvas.ActualPixels();ToolPicker.SelectedIndex=4;
        var original=session.Document;
        Check(Canvas.BeginInteraction(MouseButton.Left,new Point(38,38)),"Selection did not start.");
        Canvas.MoveInteraction(new Point(62,62));
        Check(!Canvas.FinishInteraction(MouseButton.Right,new Point(62,62))&&session.InTransaction,"Other button ended selection.");
        Check(Canvas.FinishInteraction(MouseButton.Left,new Point(62,62)),"Selection did not finish.");
        var selected=session.Document;
        Check(SelectionGeometry.Contains(selected.Selection!,new(12,12))&&!SelectionGeometry.Contains(selected.Selection!,new(40,40)),"Marquee document coordinates failed.");
        Check(session.UndoCount==1,"Selection gesture must be one Undo.");
        session.Undo();Check(ReferenceEquals(original,session.Document),"Selection Undo failed.");
        session.Redo();Check(ReferenceEquals(selected,session.Document),"Selection Redo failed.");
        ToolPicker.SelectedIndex=5;Canvas.SelectionMode=Compositor.Core.SelectionMode.Subtract;
        Canvas.BeginPointer(new(14,14));Canvas.MovePointer(new(26,26));Canvas.EndPointer(true);
        Check(!SelectionGeometry.Contains(session.Document.Selection!,new(20,20))&&SelectionGeometry.Contains(session.Document.Selection!,new(10,10)),"Ellipse subtraction failed.");
        var hollow=session.Document;
        Canvas.BeginPointer(new(0,0));Canvas.MovePointer(new(64,64));Canvas.CancelInteraction();
        Check(ReferenceEquals(hollow,session.Document)&&!session.InTransaction&&!Canvas.HasInteraction,"Cancelled selection did not restore prior outline.");
        Canvas.SelectionMode=Compositor.Core.SelectionMode.Replace;
        ToolPicker.SelectedIndex=1;BrushSize.Text="100";BrushHardness.Text="100";BrushOpacity.Text="100";BrushColor.Text="#D03040";
        Canvas.BeginPointer(new(32,32));Canvas.EndPointer(true);
        var bytes=session.ActiveLayer!.Pixels.ToRgba();
        Check(bytes[(10*64+10)*4+3]==255&&bytes[(20*64+20)*4+3]==0&&bytes[(40*64+40)*4+3]==0,"Brush ignored selection/hole.");
        var painted=session.Document;
        ClearSelectionPixels(null,new());
        var expectedClear=SelectionPixels.Blend(painted.Layers[0].Pixels,new(64,64),painted.Layers[0].Transform,SelectionCoverage.Create(painted.Selection!,64,64));
        Check(session.ActiveLayer.Pixels.ToRgba().SequenceEqual(expectedClear.ToRgba()),"Selected clear failed.");
        session.Undo();Check(ReferenceEquals(painted,session.Document),"Clear Undo failed.");
        var dialog=new ColorAdjustmentWindow(session);
        try
        {
            dialog.SetSettings(new(.2,0,0));await dialog.PendingPreview;dialog.ApplyEdit();
        }
        finally { dialog.CancelEdit();dialog.Close(); }
        Check(session.ActiveLayer.Pixels.ToRgba()[(40*64+40)*4+3]==0,"Basic adjustment wrote outside selection.");
        session.Load(original with{Selection=DocumentSelection.Empty});ToolPicker.SelectedIndex=1;
        Canvas.BeginPointer(new(32,32));Canvas.EndPointer(true);
        Check(session.UndoCount==0&&session.ActiveLayer!.Pixels.Tiles.Count==0,"Empty selection painted pixels.");
        SelectAll(null,new());InvertSelection(null,new());Check(session.Document.Selection!.IsEmpty,"Select all/invert failed.");
        Deselect(null,new());Check(session.Document.Selection is null,"Deselect failed.");
        session.Load(painted);ToolPicker.SelectedIndex=4;Canvas.Fit();UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(path,".png")))png.Save(file);
        ToolPicker.SelectedIndex=1;
        File.WriteAllText(path,JsonSerializer.Serialize(new{passed=true,checks=new[]{"rectangle/ellipse","hole subtraction","button ownership/cancel","single Undo/Redo","brush clipping","clear/Undo","basic adjustment","explicit empty","select all/invert/deselect"}}));
    }
}
