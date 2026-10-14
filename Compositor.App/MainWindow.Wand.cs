using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Compositor.Core;
using Compositor.Imaging;
namespace Compositor.App;

public partial class MainWindow
{
    private WandSettings ReadWandSettings()=>new((int)Math.Round(WandTolerance.Value),WandSample3.IsChecked==true?1:WandSample5.IsChecked==true?2:0,WandContiguous.IsChecked==true,WandAllLayers.IsChecked==true);
    private void WandAAChanged(object sender,RoutedEventArgs e){Canvas.SelectionAntialiased=WandAA.IsChecked==true;SelectionAA.IsChecked=Canvas.SelectionAntialiased;}
    private void WandModeChanged(object sender,RoutedEventArgs e)
    {
        if(Canvas?.Tool==EditorTool.MagicWand&&sender is RadioButton {Tag:string tag})Canvas.SelectionMode=(Compositor.Core.SelectionMode)int.Parse(tag);
    }
    private void WandClicked(PointD point,ModifierKeys keys)
    {
        if(busy||session.InTransaction)return;
        var settings=ReadWandSettings();var active=session.ActiveLayerId;bool aa=Canvas.SelectionAntialiased;
        var mode=keys.HasFlag(ModifierKeys.Alt)?Compositor.Core.SelectionMode.Subtract:keys.HasFlag(ModifierKeys.Shift)?Compositor.Core.SelectionMode.Add:Canvas.SelectionMode;
        subjectPending=RunSelectionAsync("Selecting matching pixels…",(document,token)=>MagicWand.Compute(document,active,point,settings,mode,aa,token));
    }
}

