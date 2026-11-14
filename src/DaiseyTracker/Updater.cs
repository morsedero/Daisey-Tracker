using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace DaiseyTracker;

/// <summary>Self-update from the repo's latest GitHub release (asset DaiseyTracker.exe).</summary>
static class Updater
{
    const string Api = "https://api.github.com/repos/morsedero/Daisey-Tracker/releases/latest";

    public static Version Current => Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    /// <summary>Local builds are 0.0.0 and never update; CI builds are 1.0.&lt;run&gt;.</summary>
    public static bool Enabled => Current.Major > 0 && Environment.ProcessPath is not null;

    public static void CleanupOld()
    {
        try { var old = Environment.ProcessPath + ".old"; if (File.Exists(old)) File.Delete(old); } catch { }
    }

    /// <summary>Downloads and swaps in a newer exe. True = restart needed.</summary>
    public static async Task<bool> TryUpdate()
    {
        if (!Enabled) return false;
        var exe = Environment.ProcessPath!;

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DaiseyTracker-Updater");

        using var doc = JsonDocument.Parse(await http.GetStringAsync(Api));
        var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
        if (!Version.TryParse(tag, out var latest) || latest <= Current) return false;

        string? url = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString() == "DaiseyTracker.exe")
                url = a.GetProperty("browser_download_url").GetString();
        if (url is null) return false;

        // A running exe can be renamed but not overwritten: download, rename current to .old, move new in.
        var tmp = exe + ".new";
        await using (var src = await http.GetStreamAsync(url))
        await using (var dst = File.Create(tmp))
            await src.CopyToAsync(dst);
        File.Move(exe, exe + ".old", true);
        File.Move(tmp, exe);
        return true;
    }

    public static void StartNew() =>
        Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--updated --autostart") { UseShellExecute = false });
}
