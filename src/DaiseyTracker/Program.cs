namespace DaiseyTracker;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        // After a self-update the old process may still be closing: wait for it.
        using var mutex = new Mutex(false, @"Local\DaiseyTracker");
        bool owned;
        try { owned = mutex.WaitOne(args.Contains("--updated") ? TimeSpan.FromSeconds(20) : TimeSpan.Zero); }
        catch (AbandonedMutexException) { owned = true; }
        if (!owned) return;

        try
        {
            Updater.CleanupOld();
            ApplicationConfiguration.Initialize();
            Application.Run(new TrayApp(openDashboard: !args.Contains("--autostart")));
        }
        finally { mutex.ReleaseMutex(); }
    }
}
