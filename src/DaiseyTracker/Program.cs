namespace DaiseyTracker;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        using var mutex = new Mutex(true, @"Local\DaiseyTracker", out var first);
        if (!first) return;
        ApplicationConfiguration.Initialize();
        Application.Run(new TrayApp(openDashboard: !args.Contains("--autostart")));
    }
}
