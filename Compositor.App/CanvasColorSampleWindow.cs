using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Compositor.Imaging;
namespace Compositor.App;

// A modal input surface over the real canvas. The editor stays disabled, so nested
// effect previews cannot accidentally paint, switch documents, or commit history.
internal sealed class CanvasColorSampleWindow : Window
{
    private readonly EditorCanvas editor;
    private readonly CompositeColorSampler sampler = new();
    private bool dragging;
    private readonly PickerSampleRing ring;
    internal Action<Color>? PreviewColor { get; set; }
    internal Color? Sampled { get; private set; }
    internal CanvasColorSampleWindow(Window owner, EditorCanvas editor, Color original)
    {
        this.editor=editor; Owner=owner;
        ring=new PickerSampleRing(original){IsHitTestVisible=false};Content=ring;
        WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
        ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;Cursor=Cursors.Cross;
        var origin=editor.PointToScreen(new Point());
        var source=PresentationSource.FromVisual(editor)!;
        var logical=source.CompositionTarget!.TransformFromDevice.Transform(origin);
        Left=logical.X;Top=logical.Y;Width=editor.ActualWidth;Height=editor.ActualHeight;
        WindowStartupLocation=WindowStartupLocation.Manual;
        MouseLeftButtonDown+=(_,e)=>{dragging=true;CaptureMouse();Sample(e.GetPosition(this));e.Handled=true;};
        MouseMove+=(_,e)=>{if(dragging)Sample(e.GetPosition(this));};
        MouseLeftButtonUp+=(_,e)=>{if(!dragging)return;Sample(e.GetPosition(this));dragging=false;ReleaseMouseCapture();DialogResult=Sampled.HasValue;e.Handled=true;};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape){e.Handled=true;DialogResult=false;}};
        LostMouseCapture+=(_,_)=>{if(dragging){dragging=false;DialogResult=false;}};
        Closed+=(_,_)=>sampler.Dispose();
    }
    internal void Sample(Point point)
    {
        if(sampler.Sample(editor.Session.Document,editor.DocumentPoint(point)) is {} color)
        {
            Sampled=Color.FromRgb(color.Red,color.Green,color.Blue);PreviewColor?.Invoke(Sampled.Value);
        }
        ring.Update(point,Sampled,editor.ShowSampleRing);
    }
}

internal sealed class PickerSampleRing : FrameworkElement
{
    private Point position;
    private readonly Color original;
    private Color current;
    internal PickerSampleRing(Color original){this.original=current=original;}
    private bool visible;
    internal void Update(Point point,Color? color,bool show)
    {position=point;current=color??current;visible=show;InvalidateVisual();}
    protected override void OnRender(DrawingContext dc)
    {
        if(!visible)return;
        dc.DrawEllipse(null,new Pen(new SolidColorBrush(Color.FromRgb(115,115,115)),24),position,43,43);
        void Half(Color color,bool top)
        {
            dc.PushClip(new RectangleGeometry(new Rect(position.X-58,position.Y+(top?-58:0),116,58)));
            dc.DrawEllipse(null,new Pen(new SolidColorBrush(color),16),position,43,43);dc.Pop();
        }
        Half(current,true);Half(original,false);
    }
}