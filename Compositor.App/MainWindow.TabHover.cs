using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;
namespace Compositor.App;
public partial class MainWindow
{
    private DispatcherTimer? tabHoverTimer;
    private Guid? tabHoverTarget;
    private IDataObject? tabHoverData;
    private long tabHoverStarted;
    private void StopTabHover(){tabHoverTimer?.Stop();tabHoverTarget=null;tabHoverData=null;}
    private void TrackTabHover(Guid? target,IDataObject data,long now)
    {
        if(target is null||target==workspace.Current.Id||!CanTransferTabLayers(target,data,Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))){StopTabHover();return;}
        if(tabHoverTarget==target)return;
        tabHoverTarget=target;tabHoverData=new DataObject(LayerDragFormat,layerDragPayload!);tabHoverStarted=now;
        if(tabHoverTimer is null)
        {
            tabHoverTimer=new DispatcherTimer(DispatcherPriority.Input){Interval=TimeSpan.FromMilliseconds(50)};
            tabHoverTimer.Tick+=(_,_)=>
            {
                bool inside=tabHoverTarget is {} id&&tabControls.TryGetValue(id,out var controls)&&new Rect(controls.Frame.RenderSize).Contains(Mouse.GetPosition(controls.Frame));
                TickTabHover(Environment.TickCount64,inside&&Mouse.LeftButton==MouseButtonState.Pressed);
            };
        }
        tabHoverTimer.Start();
    }
    private void TickTabHover(long now,bool heldInside)
    {
        if(!heldInside||tabHoverTarget is not {} target||tabHoverData is not {} data||!CanTransferTabLayers(target,data,Keyboard.Modifiers.HasFlag(ModifierKeys.Alt)))
        {StopTabHover();return;}
        if(now-tabHoverStarted<650)return;
        StopTabHover();SelectTab(target);
    }
}
