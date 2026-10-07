using System.IO;
namespace Compositor.App;
public partial class MainWindow
{
    private async Task RouteDroppedFiles(Guid? target,string[] paths,Compositor.Core.PointD? point=null)
    {
        if(!CanDropTabFiles(target))return;
        var images=new List<string>();
        async Task FlushImages()
        {
            if(images.Count==0)return;
            var batch=images.ToArray();images.Clear();
            if(target is {} id)await ImportIntoTab(id,batch,point);else await OpenImagesInTabs(batch);
        }
        foreach(var path in paths)
        {
            string normalized=Path.TrimEndingDirectorySeparator(path);
            bool recovery=normalized.EndsWith(".comp.recovery",StringComparison.OrdinalIgnoreCase);
            if(recovery||normalized.EndsWith(".comp",StringComparison.OrdinalIgnoreCase))
            {
                await FlushImages();
                await LoadProject(normalized,recovery);
            }
            else images.Add(path);
        }
        await FlushImages();
    }
}
