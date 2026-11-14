namespace DaiseyTracker;

static class Paths
{
    public static readonly string Data = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DaiseyTracker");
    public static readonly string Db = Path.Combine(Data, "events.db");
    public static readonly string Rules = Path.Combine(Data, "rules.json");
    public static readonly string SummaryDir = Path.Combine(Data, "summary");
    public static readonly string ErrorLog = Path.Combine(Data, "error.log");

    /// <summary>Create data folder; copy default rules.json beside the exe on first run.</summary>
    public static void Init()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(SummaryDir);
        if (File.Exists(Rules)) return;
        // Default rules.json is embedded in the exe.
        using var res = typeof(Paths).Assembly.GetManifestResourceStream("rules.json")!;
        using var fs = File.Create(Rules);
        res.CopyTo(fs);
    }

    /// <summary>
    /// Cloud-synced folder for summaries (read by the claude.ai dashboard), or null.
    /// Override: put a folder path in sync-folder.txt in the data folder.
    /// Default: Google Drive for desktop's "X:\My Drive\DaiseyTracker".
    /// </summary>
    public static string? SyncDir()
    {
        try
        {
            var over = Path.Combine(Data, "sync-folder.txt");
            if (File.Exists(over))
            {
                var p = File.ReadAllText(over).Trim();
                if (p.Length > 0) return p;
            }
            foreach (var d in DriveInfo.GetDrives())
            {
                var myDrive = Path.Combine(d.Name, "My Drive");
                if (Directory.Exists(myDrive)) return Path.Combine(myDrive, "DaiseyTracker");
            }
        }
        catch { }
        return null;
    }

    public static void LogError(string what, Exception ex)
    {
        try { File.AppendAllText(ErrorLog, $"{DateTime.Now:s} {what}: {ex}\n"); } catch { }
    }
}
