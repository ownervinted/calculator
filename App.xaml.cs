using System;
using System.Linq;
using System.Windows;

namespace LemieszeDesktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        if (e.Args.Length > 0 && e.Args[0] == "--update-worker")
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var code = UpdateService.RunWorker(e.Args.Skip(1).ToArray());
            Shutdown(code);
            return;
        }
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}
