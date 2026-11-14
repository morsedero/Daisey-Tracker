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
        var shipped = Path.Combine(AppContext.BaseDirectory, "rules.json");
        if (!File.Exists(Rules) && File.Exists(shipped)) File.Copy(shipped, Rules);
    }

    public static void LogError(string what, Exception ex)
    {
        try { File.AppendAllText(ErrorLog, $"{DateTime.Now:s} {what}: {ex}\n"); } catch { }
    }
}
