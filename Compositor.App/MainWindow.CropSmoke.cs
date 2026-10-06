using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private void CropSmokeTest(string path)
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        session.Load(Document.Create(100,80));Canvas.ActualPixels();ToolPicker.SelectedIndex=8;Canvas.CropSnapEnabled=false;
        var original=session.Document;
        Check(Canvas.BeginInteraction(MouseButton.Left,new Point(50,50)),"Crop did not start.");
        Canvas.MoveInteraction(new Point(90,80));
        Check(!session.InTransaction&&ReferenceEquals(original,session.Document),"Crop draft changed document.");
        Check(!Canvas.FinishInteraction(MouseButton.Right,new Point(90,80)),"Wrong button ended crop.");
        Canvas.FinishInteraction(MouseButton.Left,new Point(90,80));
        Check(Canvas.CropDraft==new CropFrame(20,20,40,30)&&!Canvas.HasInteraction,"Crop frame coordinates failed.");
        Canvas.BeginPointer(new(40,35));Canvas.MovePointer(new(45,40));Canvas.EndPointer(true);
        Check(Canvas.CropDraft==new CropFrame(25,25,40,30),"Crop move failed.");
        Canvas.BeginPointer(new(65,55));Canvas.MovePointer(new(75,65));Canvas.CancelInteraction();
        Check(Canvas.CropDraft==new CropFrame(25,25,40,30),"Cancelled resize lost prior draft.");
        ApplyCrop(null,new());
        Check(session.Document.Width==40&&session.Document.Height==30&&session.Document.Layers[0].Transform.X==-25&&session.UndoCount==1,"Crop commit failed.");
        var applied=session.Document;session.Undo();Check(ReferenceEquals(original,session.Document),"Crop Undo failed.");
        session.Redo();Check(ReferenceEquals(applied,session.Document),"Crop Redo failed.");
        session.Load(original);Canvas.ActualPixels();
        Canvas.ChangeCropRatio("1:1");Check(Canvas.CropDraft==new CropFrame(0,-10,100,100),"Ratio choice did not update draft.");
        CancelCrop(null,new());Check(Canvas.CropDraft is null&&ReferenceEquals(original,session.Document),"Cancel changed document.");
        Canvas.ChangeCropRatio("Free");
        Canvas.BeginPointer(new(30,30));Canvas.MovePointer(new(40,35),ModifierKeys.Alt);Canvas.EndPointer(true);
        Check(Canvas.CropDraft==new CropFrame(20,25,20,10),"Alt-centered crop failed.");
        ToolPicker.SelectedIndex=1;Check(Canvas.CropDraft is null,"Tool switch kept crop draft.");
        ToolPicker.SelectedIndex=8;
        Canvas.BeginPointer(new(20,20));Canvas.MovePointer(new(70,60));Canvas.EndPointer(true);
        session.Apply(d=>d.Replace(d.Layers[0] with{Name="Changed"}));
        Check(Canvas.CropDraft is null&&!Canvas.HasInteraction,"Document change kept stale crop draft.");
        session.Load(original);Canvas.ActualPixels();Canvas.CropSnapEnabled=true;
        Canvas.BeginPointer(new(20,20));Canvas.MovePointer(new(97,77));Canvas.EndPointer(true);
        Check(Canvas.CropDraft==new CropFrame(20,20,80,60),"Canvas-edge snap failed.");
        // Each visible handle must route to resizing, even on an empty canvas.
        Canvas.CropSnapEnabled=false;
        for(int i=0;i<8;i++)
        {
            CancelCrop(null,new());Canvas.BeginPointer(new(20,20));Canvas.MovePointer(new(80,60));Canvas.EndPointer(true);
            var h=CropGeometry.Handles[i];var point=new PointD(20+h.X*60,20+h.Y*40);
            Canvas.BeginPointer(point);Canvas.MovePointer(new(point.X+10,point.Y+10));Canvas.EndPointer(true);
            Check(Canvas.CropDraft!=new CropFrame(20,20,60,40)&&ReferenceEquals(original,session.Document),"Crop handle routing failed.");
        }
        var beforeCapture=Canvas.CropDraft;
        var f=beforeCapture!.Value;
        Canvas.BeginInteraction(MouseButton.Left,new Point(30+f.X+f.Width/2d,30+f.Y+f.Height/2d));
        Canvas.CaptureMouse();Canvas.MoveInteraction(new Point(300,300));
        Mouse.Capture(CropApplyButton);
        Check(!Canvas.HasInteraction&&Canvas.CropDraft==beforeCapture,"Capture loss did not roll back crop drag.");
        Mouse.Capture(null);
        Canvas.Focus();
        var source=PresentationSource.FromVisual(Canvas);
        var escape=new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent};
        WindowKeyDown(this,escape);Check(escape.Handled&&Canvas.CropDraft is null,"Escape did not cancel pending crop.");
        Canvas.BeginPointer(new(20,20));Canvas.MovePointer(new(80,60));Canvas.EndPointer(true);
        var enter=new KeyEventArgs(Keyboard.PrimaryDevice,source,Environment.TickCount,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent};
        WindowKeyDown(this,enter);Check(enter.Handled&&session.Document.Width==60&&session.Document.Height==40,"Enter did not apply crop.");
        session.Undo();Canvas.ActualPixels();
        Canvas.BeginPointer(new(20,20));Canvas.MovePointer(new(80,60));Canvas.EndPointer(true);
        Canvas.Fit();
        UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
        using(var output=File.Create(path))png.Save(output);
        CancelCrop(null,new());Canvas.ChangeCropRatio("Free");ToolPicker.SelectedIndex=1;
    }
}
