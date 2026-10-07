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
    internal Color? Sampled { get; private set; }
    internal CanvasColorSampleWindow(Window owner, EditorCanvas editor)
    {
        this.editor=editor; Owner=owner;
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
            Sampled=Color.FromRgb(color.Red,color.Green,color.Blue);
    }
}
