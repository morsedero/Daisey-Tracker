using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DaiseyTracker;

/// <summary>
/// App icon as a small PNG data URI. Desktop apps: the exe's icon. Store apps: the package logo from AppxManifest.xml.
/// Saved to icons.json so icons survive restarts.
/// </summary>
static class Icons
{
    static readonly string File_ = Path.Combine(Paths.Data, "icons-64.json"); // v2: 64px icons
    static readonly ConcurrentDictionary<string, string> Cache = Load();
    static readonly ConcurrentDictionary<string, DateTime> Missed = new(StringComparer.OrdinalIgnoreCase);

    static ConcurrentDictionary<string, string> Load()
    {
        try
        {
            if (File.Exists(File_))
                return new(JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(File_))!, StringComparer.OrdinalIgnoreCase);
        }
        catch { }
        return new(StringComparer.OrdinalIgnoreCase);
    }

    public static string? DataUri(string app)
    {
        if (Cache.TryGetValue(app, out var hit)) return hit;
        if (Missed.TryGetValue(app, out var at) && DateTime.Now - at < TimeSpan.FromMinutes(10)) return null;

        string? uri = null;
        try
        {
            var path = PathFor(app);
            if (path is not null) uri = StoreLogo(path) ?? ExeIcon(path);
        }
        catch { }

        if (uri is null) { Missed[app] = DateTime.Now; return null; }
        Cache[app] = uri;
        try { File.WriteAllText(File_, JsonSerializer.Serialize(Cache)); } catch { }
        return uri;
    }

    /// <summary>Known from foreground/audio polling, else look among running processes.</summary>
    static string? PathFor(string app)
    {
        if (Native.ExePaths.TryGetValue(app, out var p)) return p;
        foreach (var proc in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(app)))
            using (proc)
                if (Native.ImagePath((uint)proc.Id) is { } f) return Native.ExePaths[app] = f;
        return null;
    }

    static string? ExeIcon(string path)
    {
        // Large (64px) version of the exe's own icon; fall back to the 32px associated icon.
        using var ico = Icon.ExtractIcon(path, 0, 64) ?? Icon.ExtractAssociatedIcon(path);
        if (ico is null) return null;
        using var bmp = ico.ToBitmap();
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
    }

    static readonly Regex LogoAttr = new(@"Square44x44Logo=""([^""]+)""|<Logo>([^<]+)</Logo>", RegexOptions.IgnoreCase);

    /// <summary>Store app: find AppxManifest.xml above the exe, take its logo, pick the best-sized PNG variant.</summary>
    static string? StoreLogo(string exePath)
    {
        var dir = Path.GetDirectoryName(exePath);
        for (int up = 0; dir is not null && up < 4; up++, dir = Path.GetDirectoryName(dir))
        {
            var manifest = Path.Combine(dir, "AppxManifest.xml");
            if (!File.Exists(manifest)) continue;

            var m = LogoAttr.Match(File.ReadAllText(manifest));
            if (!m.Success) return null;
            var rel = (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Replace('/', '\\');
            var baseFile = Path.Combine(dir, rel);
            var folder = Path.GetDirectoryName(baseFile)!;
            var stem = Path.GetFileNameWithoutExtension(baseFile);
            if (!Directory.Exists(folder)) return null;

            // Variants: Name.scale-200.png, Name.targetsize-48.png, ... Prefer 64px, then 48px, then scale-200, then any.
            var files = Directory.GetFiles(folder, stem + "*.png");
            var best = files.FirstOrDefault(f => f.Contains("targetsize-64", StringComparison.OrdinalIgnoreCase) && !f.Contains("contrast", StringComparison.OrdinalIgnoreCase) && !f.Contains("unplated", StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(f => f.Contains("targetsize-48", StringComparison.OrdinalIgnoreCase) && !f.Contains("contrast", StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(f => f.Contains("scale-200", StringComparison.OrdinalIgnoreCase) && !f.Contains("contrast", StringComparison.OrdinalIgnoreCase))
                    ?? files.FirstOrDefault(f => !f.Contains("contrast", StringComparison.OrdinalIgnoreCase))
                    ?? (File.Exists(baseFile) ? baseFile : null);
            if (best is null || new FileInfo(best).Length > 64 * 1024) return null;
            return "data:image/png;base64," + Convert.ToBase64String(File.ReadAllBytes(best));
        }
        return null;
    }
}
