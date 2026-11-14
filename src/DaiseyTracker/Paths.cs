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
            // Exe itself inside a synced Drive folder? Use that Drive.
            var exe = Environment.ProcessPath ?? "";
            var i = exe.IndexOf(@"\My Drive\", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) return Path.Combine(exe[..(i + 9)], "DaiseyTracker");

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = DriveInfo.GetDrives().Select(d => Path.Combine(d.Name, "My Drive")).ToList(); // stream mode: G:\My Drive
            candidates.Add(Path.Combine(home, "My Drive"));                    // mirror mode
            candidates.Add(Path.Combine(home, "Google Drive", "My Drive"));
            candidates.Add(Path.Combine(home, "Google Drive"));                // old Backup and Sync
            foreach (var c in candidates)
                if (Directory.Exists(c)) return Path.Combine(c, "DaiseyTracker");
        }
        catch { }
        return null;
    }

    public static void LogError(string what, Exception ex)
    {
        try { File.AppendAllText(ErrorLog, $"{DateTime.Now:s} {what}: {ex}\n"); } catch { }
    }
}
