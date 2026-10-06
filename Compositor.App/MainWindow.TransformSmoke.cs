using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public partial class MainWindow
{
    private void TransformSmokeTest(string path)
    {
        static void Check(bool v,string m){if(!v)throw new InvalidOperationException(m);}
        var doc=Document.Create(300,240);doc=doc.Replace(doc.Layers[0] with{Pixels=LayerMask.Solid(300,240,100).Pixels});
        session.Load(doc);Canvas.ActualPixels();StartTransform(null,new());UpdateLayout();
        Check(Canvas.IsTransforming&&session.InTransaction&&TransformOptions.IsVisible,"Transform mode did not start.");
        var title=ToolTitle.TranslatePoint(new Point(),this);double canvasTop=Canvas.TranslatePoint(new Point(),this).Y;
        Check(Canvas.BeginInteraction(MouseButton.Left,new Point(330,270)),"Bottom-right handle did not start.");
        Canvas.MovePointer(new(330,270));Canvas.EndPointer(true);
        Check(Canvas.IsTransforming&&!Canvas.HasInteraction&&session.UndoCount==0,"Handle release ended transform edit.");
        Check(Canvas.TransformDraft!.Width>300,"Handle did not resize.");
        var before=Canvas.TransformDraft;TransformAngleSlider.Value=30;Check(Canvas.TransformDraft!.Rotation==30,"Rotation slider failed.");
        TransformFlipX(this,new());Check(Canvas.TransformDraft!.FlipX,"Flip button failed.");
        CancelTransform(null,new());Check(ReferenceEquals(doc,session.Document)&&!session.InTransaction,"Transform cancellation failed.");
        for(int i=0;i<8;i++)
        {
            StartTransform(null,new());
            var t=Canvas.TransformDraft!;var p=TransformDrag.Point(t,CropGeometry.Handles[i]);
            Canvas.UpdateTransformCursor(p);
            Check(Canvas.Cursor==((i%4) switch{0=>Cursors.SizeNWSE,1=>Cursors.SizeNS,2=>Cursors.SizeNESW,_=>Cursors.SizeWE}),"Handle cursor direction failed.");
            Check(Canvas.BeginInteraction(MouseButton.Left,new Point(p.X+30,p.Y+30)),"Transform handle hit failed.");
            Canvas.MovePointer(new(p.X+12,p.Y+7));Canvas.EndPointer(true);CancelTransform(null,new());
        }
        StartTransform(null,new());
        var rotation=TransformDrag.Point(Canvas.TransformDraft!,new(.5,-30d/240));
        Check(Canvas.BeginInteraction(MouseButton.Left,new Point(rotation.X+30,rotation.Y+30)),"Rotation handle hit failed.");
        Canvas.MovePointer(new(300,120));Canvas.EndPointer(true);
        Check(Math.Abs(Canvas.TransformDraft!.Rotation)>10,"Rotation handle did not rotate.");
        Canvas.Focus();WindowKeyDown(this,new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(Canvas),Environment.TickCount,Key.Escape){RoutedEvent=Keyboard.KeyDownEvent});
        Check(ReferenceEquals(doc,session.Document),"Escape failed.");
        session.Load(doc with{Selection=SelectionGeometry.Box(60,60,100,80,false,false)});
        var selected=session.Document;StartTransform(null,new());
        TransformScaleSlider.Value=150;TransformAngleSlider.Value=20;
        UpdateLayout();Check(Math.Abs(Canvas.TranslatePoint(new Point(),this).Y-canvasTop)<.1&&(ToolTitle.TranslatePoint(new Point(),this)-title).Length<.1,"Transform UI moved canvas or title.");
        var bitmap=new RenderTargetBitmap((int)ActualWidth,(int)ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(this);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var output=File.Create(path))png.Save(output);
        Canvas.Focus();WindowKeyDown(this,new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(Canvas),Environment.TickCount,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
        Check(!Canvas.IsTransforming&&session.UndoCount==1,"Enter did not commit one transform.");
        var applied=session.Document;session.Undo();Check(ReferenceEquals(selected,session.Document),"Selection transform Undo failed.");session.Redo();Check(ReferenceEquals(applied,session.Document),"Selection transform Redo failed.");
        session.Load(doc);StartTransform(null,new());TransformAngleSlider.Value=15;ToolPicker.SelectedIndex=1;
        Check(!Canvas.IsTransforming&&!session.InTransaction&&session.UndoCount==1,"Tool switch did not commit.");
        var second=Layer.Blank("Other",300,240);
        session.Load(doc with{Layers=doc.Layers.Add(second)},doc.Layers[0].Id);
        StartTransform(null,new());TransformAngleSlider.Value=12;
        Layers.SelectedItem=Layers.Items.Cast<LayerRow>().Single(r=>r.Id==second.Id);
        Check(!Canvas.IsTransforming&&session.ActiveLayerId==second.Id&&session.UndoCount==1,"Layer switch lost clicked layer or transform commit.");
        session.Load(doc);StartTransform(null,new());var prior=Canvas.TransformDraft!;
        Canvas.BeginInteraction(MouseButton.Left,new Point(180,150));Canvas.MovePointer(new(180,150));Canvas.CancelInteraction();
        Check(Canvas.IsTransforming&&Canvas.TransformDraft==prior,"Interrupted drag did not restore prior draft.");
        Canvas.BeginInteraction(MouseButton.Left,new Point(180,150));Canvas.CaptureMouse();Canvas.MovePointer(new(190,160));
        Mouse.Capture(TransformScale);Check(!Canvas.HasInteraction&&Canvas.TransformDraft==prior,"Lost capture did not restore transform draft.");Mouse.Capture(null);
        Check(Canvas.BeginInteraction(MouseButton.Middle,new Point(100,100)),"Transform blocked middle pan.");
        Canvas.MoveInteraction(new Point(110,110));Canvas.FinishInteraction(MouseButton.Middle,new Point(110,110));
        Check(Canvas.IsTransforming&&Canvas.TransformDraft==prior,"Panning changed transform.");CancelTransform(null,new());
    }
}
