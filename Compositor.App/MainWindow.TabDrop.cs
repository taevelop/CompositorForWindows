using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Compositor.App;
public partial class MainWindow
{
    private bool CanDropTabFiles(Guid target)=>!busy&&workspace.Documents.Any(t=>t.Id==target)&&
        (!session.InTransaction||Canvas.IsTransforming);
    private void AttachTabDrop(Border frame,Guid target)
    {
        frame.AllowDrop=true;
        frame.DragOver+=(_,e)=>
        {
            e.Handled=true;
            bool valid=CanDropTabFiles(target)&&e.Data.GetDataPresent(DataFormats.FileDrop)&&e.AllowedEffects.HasFlag(DragDropEffects.Copy);
            e.Effects=valid?DragDropEffects.Copy:DragDropEffects.None;
            frame.BorderThickness=valid?new Thickness(2):new Thickness(0,0,0,2);
            if(valid)frame.BorderBrush=(Brush)FindResource("Accent");
        };
        frame.DragLeave+=(_,e)=>{frame.BorderThickness=new(0,0,0,2);RefreshTabs();e.Handled=true;};
        frame.Drop+=async(_,e)=>
        {
            e.Handled=true;frame.BorderThickness=new(0,0,0,2);e.Effects=DragDropEffects.None;
            if(e.AllowedEffects.HasFlag(DragDropEffects.Copy)&&e.Data.GetData(DataFormats.FileDrop) is string[] paths&&CanDropTabFiles(target))
            {e.Effects=DragDropEffects.Copy;await ImportIntoTab(target,paths);}
            RefreshTabs();
        };
    }
    private async Task ImportIntoTab(Guid target,string[] paths)
    {
        if(paths.Length==0||!CanDropTabFiles(target))return;
        SelectTab(target);
        if(workspace.Current.Id!=target)return;
        await Import(paths);
    }
}
