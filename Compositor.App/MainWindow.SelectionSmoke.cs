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
        session.Load(original);ToolPicker.SelectedIndex=6;
        Canvas.BeginPointer(new(5,5));Canvas.MovePointer(new(45,5));Canvas.MovePointer(new(5,45));Canvas.EndPointer(true);
        Check(SelectionGeometry.Contains(session.Document.Selection!,new(10,10))&&!SelectionGeometry.Contains(session.Document.Selection!,new(40,40)),"Freehand outline failed.");
        var triangle=session.Document;
        Canvas.BeginPointer(new(10,10));Canvas.MovePointer(new(90.4,10));Canvas.EndPointer(true);
        Check(SelectionGeometry.Contains(session.Document.Selection!,new(90,10))&&!SelectionGeometry.Contains(session.Document.Selection!,new(10,10)),"Outline movement was clipped to canvas.");
        Check(ReferenceEquals(triangle.Layers[0].Pixels,session.ActiveLayer!.Pixels),"Outline move changed pixels.");
        session.Undo();Check(ReferenceEquals(triangle,session.Document),"Outline move Undo failed.");
        NudgeSelection(Key.Right,10);
        Check(SelectionGeometry.Contains(session.Document.Selection!,new(20,10)),"Outline nudge failed.");
        session.Undo();
        Canvas.BeginPointer(new(10,10));Canvas.EndPointer(true);Check(session.Document.Selection is null,"Click inside selection did not deselect.");
        ToolPicker.SelectedIndex=4;Canvas.SelectionFromCenter=true;
        Canvas.BeginPointer(new(30.4,30.4));Canvas.MovePointer(new(40.4,35.4));Canvas.EndPointer(true);Canvas.SelectionFromCenter=false;
        Check(SelectionGeometry.Contains(session.Document.Selection!,new(20.5,25.5))&&!SelectionGeometry.Contains(session.Document.Selection!,new(19.5,25.5)),"Centered marquee or pixel snapping failed.");
        session.Load(original);Canvas.BeginPointer(new(1,1));Canvas.EndPointer(true);
        Check(session.Document.Selection is null&&session.UndoCount==0,"Zero-area click created a blank restriction.");
        foreach(var operation in Enum.GetValues<SelectionModification>())
        {
            var before=original with {Selection=SelectionGeometry.Box(16,16,32,32)};
            session.Load(before);
            var editor=new SelectionModifyWindow(session,operation){Owner=this};
            try
            {
                editor.Show();Check(((SolidColorBrush)editor.Background).Color==Color.FromRgb(28,30,34),"Selection editor lost dark theme.");editor.SetAmount(4);await editor.PendingPreview;
                Check(session.Document.Selection!=before.Selection,"Selection modifier did not preview.");
                editor.SetPreviewVisible(false);Check(ReferenceEquals(before,session.Document),"Selection comparison failed.");
                editor.ApplyEdit();
                Check(session.UndoCount==1&&!session.InTransaction,"Selection modifier did not commit one Undo.");
                var applied=session.Document;
                session.Undo();Check(ReferenceEquals(before,session.Document),"Selection modifier Undo failed.");
                session.Redo();Check(ReferenceEquals(applied,session.Document),"Selection modifier Redo failed.");
            }
            finally {editor.CancelEdit();editor.Close();}
            session.Load(before);
            var cancelled=new SelectionModifyWindow(session,operation){Owner=this};
            try
            {
                cancelled.Show();cancelled.SetAmount(6);await cancelled.PendingPreview;
                if(operation==SelectionModification.Feather)
                {
                    cancelled.UpdateLayout();
                    var shot=new RenderTargetBitmap((int)cancelled.ActualWidth,(int)cancelled.ActualHeight,96,96,PixelFormats.Pbgra32);shot.Render(cancelled);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(shot));using var file=File.Create(Path.ChangeExtension(path,".feather.png"));encoder.Save(file);
                }
                cancelled.CancelEdit();
                Check(ReferenceEquals(before,session.Document)&&session.UndoCount==0,"Selection modifier cancel changed state.");
            }
            finally {cancelled.Close();}
        }        var identitySource=original with{Selection=SelectionGeometry.Box(8,8,32,32)};
        session.Load(identitySource);
        var identityEditor=new SelectionModifyWindow(session,SelectionModification.Feather);
        try
        {
            identityEditor.SetAmount(0);await identityEditor.PendingPreview;identityEditor.ApplyEdit();
            Check(ReferenceEquals(identitySource,session.Document)&&session.UndoCount==0,"Zero-radius modifier changed history.");
        }
        finally {identityEditor.CancelEdit();identityEditor.Close();}
        var racingEditor=new SelectionModifyWindow(session,SelectionModification.Feather);
        racingEditor.SetAmount(100);racingEditor.SetAmount(2);var lastPreview=racingEditor.PendingPreview;
        racingEditor.CancelEdit();racingEditor.Close();await lastPreview;
        Check(ReferenceEquals(identitySource,session.Document)&&!session.InTransaction,"Late preview restored a cancelled edit.");        session.Load(original);Canvas.ActualPixels();ToolPicker.SelectedIndex=7;Canvas.Focus();
        void PolygonClick(double x,double y)
        {
            var p=new Point(x+30,y+30);
            Check(Canvas.BeginInteraction(MouseButton.Left,p),"Polygon click rejected.");
            Check(Canvas.CaptureMouse(),"Polygon mouse capture failed.");
            Check(Canvas.FinishInteraction(MouseButton.Left,p),"Polygon mouse release failed.");
            Canvas.ReleaseGestureCapture();
            Check(Canvas.PolygonActive&&session.InTransaction&&!Canvas.IsMouseCaptured,"Mouse release ended polygon draft or retained capture.");
        }
        PolygonClick(5,5);PolygonClick(45,5);PolygonClick(5,45);
        Canvas.MoveInteraction(new Point(90,90));
        Check(ReferenceEquals(original,session.Document)&&session.UndoCount==0,"Polygon draft edited committed selection.");
        Check(!Canvas.BeginInteraction(MouseButton.Right,new Point(80,80)),"Foreign mouse button added a vertex.");
        Check(Canvas.HandleSelectionKey(Key.Back),"Backspace did not remove a vertex.");
        PolygonClick(45,45);Check(Canvas.HandleSelectionKey(Key.Enter),"Enter did not close polygon.");
        Check(!Canvas.HasInteraction&&!session.InTransaction&&session.UndoCount==1,"Polygon left interaction open.");
        Check(SelectionGeometry.Contains(session.Document.Selection!,new(35,15))&&!SelectionGeometry.Contains(session.Document.Selection!,new(10,40)),"Polygon corners or Backspace incorrect.");
        var polygon=session.Document;session.Undo();Check(ReferenceEquals(original,session.Document),"Polygon Undo failed.");
        session.Redo();Check(ReferenceEquals(polygon,session.Document),"Polygon Redo failed.");
        session.Load(original);PolygonClick(5,5);PolygonClick(45,5);PolygonClick(5,45);
        Check(Canvas.BeginInteraction(MouseButton.Left,new Point(35.5,35.5))&&!Canvas.HasInteraction,"Click near first vertex did not close.");
        Check(SelectionGeometry.Contains(session.Document.Selection!,new(10,10)),"Near-origin closure lost shape.");
        session.Load(original);PolygonClick(5,5);PolygonClick(45,5);PolygonClick(5,45);
        Check(Canvas.BeginInteraction(MouseButton.Left,new Point(90,90),2)&&!Canvas.HasInteraction,"Double-click did not close.");
        Check(!SelectionGeometry.Contains(session.Document.Selection!,new(40,40)),"Double-click added an unwanted final vertex.");
        session.Load(original);PolygonClick(5,5);Canvas.HandleSelectionKey(Key.Delete);
        Check(!Canvas.HasInteraction&&!session.InTransaction&&ReferenceEquals(original,session.Document),"Deleting final vertex did not cancel.");
        PolygonClick(5,5);PolygonClick(45,5);Canvas.HandleSelectionKey(Key.Escape);
        Check(!Canvas.HasInteraction&&session.UndoCount==0,"Esc added history.");
        PolygonClick(5,5);Check(Canvas.CaptureMouse(),"Capture-loss setup failed.");Canvas.ReleaseMouseCapture();
        Check(!Canvas.HasInteraction&&!session.InTransaction,"External capture loss did not cancel polygon.");
        PolygonClick(5,5);
        Check(Canvas.BeginInteraction(MouseButton.Left,new Point(75,35)),"Held-click setup failed.");
        Check(Canvas.CaptureMouse(),"Held-click capture failed.");
        Canvas.HandleSelectionKey(Key.Back);
        Check(!Canvas.IsMouseCaptured&&Canvas.PolygonActive,"Backspace during press retained capture.");
        PolygonClick(45,5);Canvas.HandleSelectionKey(Key.Escape);
        PolygonClick(5,5);ToolPicker.SelectedIndex=1;
        Check(!Canvas.HasInteraction&&!session.InTransaction,"Tool switch did not cancel polygon.");        var moveDoc=original.Replace(original.Layers[0] with{Pixels=Raster.FromRgba(2,1,[180,20,10,255,0,0,0,0]),Transform=new(10,10,2,1)}) with{Selection=SelectionGeometry.Box(10,10,1,1,false,false)};
        foreach(bool duplicate in new[]{false,true})
        {
            session.Load(moveDoc);Canvas.ActualPixels();ToolPicker.SelectedIndex=4;
            Check(Canvas.BeginInteraction(MouseButton.Left,new Point(40.5,40.5),modifiers:ModifierKeys.Control|(duplicate?ModifierKeys.Alt:ModifierKeys.None)),"Pixel drag did not start.");
            Canvas.MoveInteraction(new Point(45.5,40.5));Check(Canvas.HasInteraction,"Pixel drag lost ownership.");
            Canvas.FinishInteraction(MouseButton.Left,new Point(45.5,40.5));
            Check(!Canvas.HasInteraction&&!session.InTransaction&&session.UndoCount==1,"Pixel drag did not commit once.");
            Check(session.ActiveLayer!.Transform.X==(duplicate?10:15)&&session.ActiveLayer.Pixels.Width==(duplicate?6:1),"Move/duplicate result incorrect.");
            session.Undo();Check(ReferenceEquals(moveDoc,session.Document),"Pixel drag Undo failed.");
            Canvas.BeginInteraction(MouseButton.Left,new Point(40.5,40.5),modifiers:ModifierKeys.Control);
            Canvas.MoveInteraction(new Point(60,50));Canvas.CancelInteraction();
            Check(ReferenceEquals(moveDoc,session.Document)&&!session.InTransaction&&!Canvas.HasInteraction,"Pixel drag cancellation failed.");
        }
        session.Load(moveDoc);NudgeSelectedPixels(Key.Right,10);
        Check(session.ActiveLayer!.Transform.X==20&&session.UndoCount==1,"Pixel nudge failed.");
        session.Undo();        session.Load(painted);ToolPicker.SelectedIndex=4;Canvas.Fit();UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.ChangeExtension(path,".png")))png.Save(file);
        ToolPicker.SelectedIndex=1;
        File.WriteAllText(path,JsonSerializer.Serialize(new{passed=true,checks=new[]{"rectangle/ellipse/center/snap","freehand/polygonal lasso","polygon capture/Enter/Backspace/Esc/double-click/near-origin/tool-switch","outline move/nudge/click deselect","feather/expand/contract preview/compare/apply/cancel","hole subtraction","button ownership/cancel","single Undo/Redo","pixel move/duplicate/cancel/nudge","brush clipping","clear/Undo","basic adjustment","explicit empty","select all/invert/deselect"}}));
    }
}
