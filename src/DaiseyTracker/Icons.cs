using System.Collections.Concurrent;
using System.Drawing.Imaging;

namespace DaiseyTracker;

/// <summary>App icon as a small PNG data URI, from the exe's own icon.</summary>
static class Icons
{
    static readonly ConcurrentDictionary<string, string?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static string? DataUri(string app)
    {
        if (Cache.TryGetValue(app, out var hit)) return hit;
        if (!Native.ExePaths.TryGetValue(app, out var path)) return null; // not seen yet; try again later
        string? uri = null;
        try
        {
            using var ico = Icon.ExtractAssociatedIcon(path);
            if (ico is not null)
            {
                using var bmp = ico.ToBitmap();
                using var ms = new MemoryStream();
                bmp.Save(ms, ImageFormat.Png);
                uri = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
        }
        catch { }
        Cache[app] = uri;
        return uri;
    }
}
