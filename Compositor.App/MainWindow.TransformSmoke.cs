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
        var group=Layer.Group("Transform group",300,240);
        var grouped=LayerHierarchy.Wrap(doc,doc.Layers[0].Id,group);
        grouped=grouped with{Layers=grouped.Layers.Add(Layer.Blank("Hidden",10,10) with{Visible=false,ParentId=group.Id,Transform=new(-100,-100,10,10)})};
        session.Load(grouped,group.Id);StartTransform(null,new());
        Check(Canvas.TransformDraft==new LayerTransform(0,0,300,240),"Group transform used hidden layers or folder metadata.");
        TransformScaleSlider.Value=150;TransformAngleSlider.Value=30;ApplyTransform(null,new());
        var child=session.Document.Layers.First(l=>l.Id==doc.Layers[0].Id);
        Check(Math.Abs(child.Transform.Width-450)<.001&&Math.Abs(child.Transform.Rotation-30)<.001,"Group sliders did not transform child.");
        Check(session.Document.Layers[^1].Transform.X==-100&&session.UndoCount==1,"Group changed hidden layer or split Undo.");
        session.Undo();Check(ReferenceEquals(grouped,session.Document),"Group transform Undo failed.");
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
        // Exercise actual ListBox selection events without injecting global keyboard input.
        var third=Layer.Blank("Third",20,20) with{Transform=new(340,0,20,20)};
        var multi=doc with{Layers=doc.Layers.Add(second with{Transform=new(310,0,20,20)}).Add(third)};
        session.Load(multi,doc.Layers[0].Id);ToolPicker.SelectedIndex=0;
        var itemsSource=Layers.ItemsSource;
        Layers.SelectedItems.Add(Layers.Items.Cast<LayerRow>().Single(r=>r.Id==second.Id));
        Check(session.SelectedLayerIds.Count==2&&session.ActiveLayerId==second.Id,"ListBox additive selection failed.");
        Check(ReferenceEquals(itemsSource,Layers.ItemsSource),"Selection reset the native range anchor.");
        StartTransform(null,new());Check(Canvas.TransformDraft!.Width==330,"Multiple transform used only active layer.");
        TransformAngleSlider.Value=10;
        Layers.SelectedItems.Add(Layers.Items.Cast<LayerRow>().Single(r=>r.Id==third.Id));
        Check(!Canvas.IsTransforming&&session.UndoCount==1&&session.SelectedLayerIds.Count==3&&session.ActiveLayerId==third.Id,"Selection change lost selection while committing transform.");
        session.Undo();Check(session.SelectedLayerIds.Count==2&&Layers.SelectedItems.Count==2,"Undo did not restore multi-selection in panel.");
        Canvas.BeginPointer(new(0,0));Canvas.MovePointer(new(12,8));Canvas.EndPointer(true);
        Check(session.Document.Layers[0].Transform.X==12&&session.Document.Layers[1].Transform.X==322&&session.Document.Layers[2].Transform.X==340,"Move did not move exactly the selected layers.");
        session.Undo();Check(ReferenceEquals(multi,session.Document),"Multiple move Undo failed.");
        DeleteLayer(null,new());Check(session.Document.Layers.Length==1&&session.Document.Layers[0].Id==third.Id,"Delete did not remove all selected layers.");
        session.Undo();Check(session.Document.Layers.Length==3&&Layers.SelectedItems.Count==2,"Delete Undo did not restore selection.");
        Layers.SelectAll();Check(session.SelectedLayerIds.Count==3,"Native select-all failed.");
        Layers.UnselectAll();Check(session.ActiveLayerId is null&&session.SelectedLayerIds.Count==0,"Native clear selection failed.");
        session.Load(doc);
    }
}
