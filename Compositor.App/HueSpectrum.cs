using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;

internal sealed class HueSpectrum : FrameworkElement
{
    public HueSaturationAdjustment Settings { get; set; } = new();
    public Action<HueBand>? BandChanged { get; set; }
    private int selected;
    private bool dragging;
    public HueSpectrum(){Height=80;Focusable=true;Cursor=Cursors.SizeWE;LostMouseCapture+=(_,_)=>dragging=false;}
    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        for(int i=0;i<72;i++)
        {
            double hue=i*5,shift=Settings.Adjustments.Sum(p=>p.Value.Hue*Settings.Weight(p.Key,hue));
            Brush Color(double h){var (r,g,b)=HueSaturationProcessor.ToRgb(HueBand.Wrap(h),1,.5);return new SolidColorBrush(System.Windows.Media.Color.FromRgb((byte)(r*255),(byte)(g*255),(byte)(b*255)));}
            dc.DrawRectangle(Color(hue),null,new Rect(i*ActualWidth/72,0,ActualWidth/72+1,18));
            dc.DrawRectangle(Color(hue+shift),null,new Rect(i*ActualWidth/72,52,ActualWidth/72+1,18));
        }
        var band=Settings.Band(Settings.Range);var handles=band.Handles;
        for(int i=0;i<4;i++)
        {
            double x=HueBand.Wrap(handles[i])/360*ActualWidth;
            var brush=i==selected&&IsKeyboardFocusWithin?new SolidColorBrush(Color.FromRgb(108,154,224)):Brushes.White;
            dc.DrawRectangle(brush,null,new Rect(Math.Clamp(x-2,0,Math.Max(0,ActualWidth-4)),i is 1 or 2?22:30,4,i is 1 or 2?26:10));
        }
    }
    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);if(e.ChangedButton!=MouseButton.Left||ActualWidth<=0)return;
        Focus();double h=Math.Clamp(e.GetPosition(this).X/ActualWidth*360,0,360);
        selected=Enumerable.Range(0,4).MinBy(i=>Math.Abs(HueBand.Wrap(Settings.Band(Settings.Range).Handles[i])-h));
        dragging=CaptureMouse();Move(h);e.Handled=true;
    }
    private void Move(double hue){BandChanged?.Invoke(Settings.Band(Settings.Range).MoveHandle(selected,hue));InvalidateVisual();}
    protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging&&ActualWidth>0)Move(Math.Clamp(e.GetPosition(this).X/ActualWidth*360,0,360));}
    protected override void OnMouseUp(MouseButtonEventArgs e){base.OnMouseUp(e);if(e.ChangedButton==MouseButton.Left&&dragging){dragging=false;ReleaseMouseCapture();e.Handled=true;}}
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if(e.Key is Key.Left or Key.Right){Move(Settings.Band(Settings.Range).Handles[selected]+(e.Key==Key.Left?-1:1)*((Keyboard.Modifiers&ModifierKeys.Shift)!=0?10:1));e.Handled=true;}
        else if(e.Key is Key.Up or Key.Down){selected=(selected+(e.Key==Key.Up?3:1))%4;InvalidateVisual();e.Handled=true;}
    }
}
