using System.Collections.Concurrent;
using System.Drawing.Imaging;
using System.Text.Json;

namespace DaiseyTracker;

/// <summary>App icon as a small PNG data URI, from the exe's own icon. Saved to icons.json so icons survive restarts.</summary>
static class Icons
{
    static readonly string File_ = Path.Combine(Paths.Data, "icons.json");
    static readonly ConcurrentDictionary<string, string> Cache = Load();

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
        if (!Native.ExePaths.TryGetValue(app, out var path)) return null; // not seen since start; try again later
        try
        {
            using var ico = Icon.ExtractAssociatedIcon(path);
            if (ico is null) return null;
            using var bmp = ico.ToBitmap();
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            var uri = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            Cache[app] = uri;
            File.WriteAllText(File_, JsonSerializer.Serialize(Cache));
            return uri;
        }
        catch { return null; }
    }
}
