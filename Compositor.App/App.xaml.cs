using System.Windows;

namespace Compositor.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow(); MainWindow = window;
        if (e.Args.Length == 2 && e.Args[0] == "--smoke")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            window.ShowInTaskbar = false; window.Left = -10000; window.Top = -10000;
            window.Loaded += async (_, _) =>
            {
                try { await window.SmokeTest(e.Args[1]); Shutdown(0); }
                catch (Exception error) { System.IO.File.WriteAllText(e.Args[1] + ".error.txt", error.ToString()); Shutdown(1); }
            };
        }
        window.Show();
    }
}
