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
        if (!File.Exists(exe))
            throw new MovedException();

        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("DaiseyTracker-Updater");

        // Conditional request: "304 Not Modified" doesn't count against GitHub's rate limit, so polling every minute is fine.
        using var req = new HttpRequestMessage(HttpMethod.Get, Api);
        if (_etag is not null) req.Headers.TryAddWithoutValidation("If-None-Match", _etag);
        using var resp = await http.SendAsync(req);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotModified) return false;
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadAsStringAsync();
        var etag = resp.Headers.ETag?.Tag;

        using var doc = JsonDocument.Parse(body);
        var tag = doc.RootElement.GetProperty("tag_name").GetString()?.TrimStart('v') ?? "";
        if (!Version.TryParse(tag, out var latest) || latest <= Current) { _etag = etag; return false; }

        string? url = null;
        foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            if (a.GetProperty("name").GetString() == "DaiseyTracker.exe")
                url = a.GetProperty("browser_download_url").GetString();
        if (url is null) return false;

        // Download into our own data folder (always writable).
        var dir = Path.Combine(Paths.Data, "update");
        Directory.CreateDirectory(dir);
        var tmp = Path.Combine(dir, "DaiseyTracker.exe");
        await using (var src = await http.GetStreamAsync(url))
        await using (var dst = File.Create(tmp))
            await src.CopyToAsync(dst);

        // Fast path: a running exe can be renamed but not overwritten.
        try
        {
            File.Move(exe, exe + ".old", true);
            File.Move(tmp, exe);
            _script = null;
        }
        catch (Exception ex)
        {
            // Fallback: a script waits for us to exit, then copies the new exe over and restarts it.
            Paths.LogError("update-swap (using fallback script)", ex);
            if (File.Exists(exe + ".old") && !File.Exists(exe)) File.Move(exe + ".old", exe);
            _script = Path.Combine(dir, "swap.cmd");
            File.WriteAllText(_script,
                "@echo off\r\n" +
                $"for /l %%i in (1,1,30) do (copy /y \"{tmp}\" \"{exe}\" >nul 2>&1 && goto ok & timeout /t 1 /nobreak >nul)\r\n" +
                "exit /b 1\r\n" +
                $":ok\r\nstart \"\" \"{exe}\" --updated --autostart\r\n");
        }
        return true;
    }

    static string? _script;
    static string? _etag; // last "no newer version" response

    public sealed class MovedException() : Exception("The app was moved or deleted while running. Quit it and start it again from its new folder.");

    public static void StartNew()
    {
        if (_script is null)
            Process.Start(new ProcessStartInfo(Environment.ProcessPath!, "--updated --autostart") { UseShellExecute = false });
        else
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{_script}\"") { UseShellExecute = false, CreateNoWindow = true });
    }
}
