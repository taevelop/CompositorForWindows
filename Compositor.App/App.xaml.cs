using System.Windows;

namespace Compositor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(); MainWindow = window;
        if (e.Args.Length == 2 && e.Args[0] is "--smoke" or "--render-benchmark" or "--gradient-benchmark")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window.ShowInTaskbar = false; window.Left = -10000; window.Top = -10000;
            window.Loaded += async (_, _) =>
            {
                try { if(e.Args[0]=="--gradient-benchmark")await window.GradientBenchmark(e.Args[1]);else if(e.Args[0]=="--render-benchmark")await window.RenderPreparationBenchmark(e.Args[1]);else await window.SmokeTest(e.Args[1]); System.IO.File.Delete(e.Args[1] + ".error.txt"); Shutdown(0); }
                catch (Exception error) { System.IO.File.WriteAllText(e.Args[1] + ".error.txt", error.ToString()); window.CloseSmokeOnFailure(); Shutdown(1); }
            };
        }
        window.Show();
    }
}
