using System.Windows;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;
public sealed partial class EditorCanvas
{
    private readonly CompositeColorSampler colorSampler=new();
    private bool samplingColor;
    public Action<SampledColor>? ColorSampled {get;set;}
    internal bool IsSamplingColor=>samplingColor;
    private void SampleDocumentColor(PointD point)
    {
        if(colorSampler.Sample(Session.Document,point) is {} color)ColorSampled?.Invoke(color);
    }
    private void SampleAt(Point point){Cursor=Cursors.Cross;SampleDocumentColor(DocumentPoint(point));}
    private void EndColorSampling()
    {
        samplingColor=false;gestureButton=null;ReleaseGestureCapture();
        Cursor=Tool==EditorTool.Eyedropper?Cursors.Cross:null;
    }
}
