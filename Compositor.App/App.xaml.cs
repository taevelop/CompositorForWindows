using System.Windows;

namespace Compositor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(); MainWindow = window;
        if(e.Args.Length==3&&e.Args[0] is "--clone-benchmark" or "--healing-benchmark")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;window.ShowInTaskbar=false;window.Left=-10000;window.Top=-10000;
            window.Loaded+=async(_,_)=>
            {
                try{if(e.Args[0]=="--healing-benchmark")await window.HealingBenchmark(e.Args[1],int.Parse(e.Args[2]));else await window.CloneBenchmark(e.Args[1],int.Parse(e.Args[2]));Shutdown(0);}
                catch(Exception error){System.IO.File.WriteAllText(e.Args[1]+".error.txt",error.ToString());window.CloseSmokeOnFailure();Shutdown(1);}
            };
        }
        else if(e.Args.Length==4&&e.Args[0]=="--background-smoke")
        {
            ShutdownMode=ShutdownMode.OnExplicitShutdown;window.ShowInTaskbar=false;window.Left=-10000;window.Top=-10000;
            window.Loaded+=async(_,_)=>
            {
                try{await window.BackgroundModelSmoke(e.Args[1],e.Args[2],e.Args[3]);Shutdown(0);}
                catch(Exception error){System.IO.File.WriteAllText(e.Args[1]+".error.txt",error.ToString());window.CloseSmokeOnFailure();Shutdown(1);}
            };
        }
        else if (e.Args.Length == 2 && e.Args[0] is "--smoke" or "--blur-smoke" or "--healing-smoke" or "--clone-smoke" or "--tools-smoke" or "--render-benchmark" or "--gradient-benchmark" or "--shape-benchmark")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window.ShowInTaskbar = false; window.Left = -10000; window.Top = -10000;
            window.Loaded += async (_, _) =>
            {
                try { if(e.Args[0]=="--blur-smoke")await window.BlurToolSmoke(e.Args[1]);else if(e.Args[0]=="--healing-smoke")await window.HealingToolSmoke(e.Args[1]);else if(e.Args[0]=="--clone-smoke")await window.CloneToolSmoke(e.Args[1]);else if(e.Args[0]=="--tools-smoke")await window.ToolsSmoke(e.Args[1]);else if(e.Args[0]=="--shape-benchmark")await window.ShapeBenchmark(e.Args[1]);else if(e.Args[0]=="--gradient-benchmark")await window.GradientBenchmark(e.Args[1]);else if(e.Args[0]=="--render-benchmark")await window.RenderPreparationBenchmark(e.Args[1]);else await window.SmokeTest(e.Args[1]); System.IO.File.Delete(e.Args[1] + ".error.txt"); Shutdown(0); }
                catch (Exception error) { System.IO.File.WriteAllText(e.Args[1] + ".error.txt", error.ToString()); window.CloseSmokeOnFailure(); Shutdown(1); }
            };
        }
        window.Show();
    }
}
