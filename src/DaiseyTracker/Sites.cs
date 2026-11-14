using System.Text.RegularExpressions;

namespace DaiseyTracker;

/// <summary>Guesses a site name from a browser tab title ("How to cook rice - YouTube" -> "YouTube"). Never returns the full title.</summary>
static class Sites
{
    static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase)
        { "chrome.exe", "msedge.exe", "firefox.exe", "brave.exe", "opera.exe", "vivaldi.exe", "arc.exe" };

    static readonly Regex BrowserSuffix = new(
        @"\s+[-—–]\s+(Google Chrome|Microsoft​? Edge|Mozilla Firefox|Brave|Opera|Vivaldi|Arc)$", RegexOptions.IgnoreCase);
    static readonly Regex EdgeExtra = new(@"\s+and \d+ more pages?.*$", RegexOptions.IgnoreCase);
    static readonly Regex Counter = new(@"^\(\d+\+?\)\s*");
    static readonly string[] Seps = { " - ", " — ", " – ", " | ", " · ", " • " };

    public static bool IsBrowser(string app) => Browsers.Contains(app);

    public static string FromTitle(string title)
    {
        var t = BrowserSuffix.Replace(title.Trim(), "");
        t = EdgeExtra.Replace(t, "");
        t = Counter.Replace(t, "").Trim();

        int cut = -1, len = 0;
        foreach (var s in Seps)
        {
            var i = t.LastIndexOf(s, StringComparison.Ordinal);
            if (i > cut) { cut = i; len = s.Length; }
        }
        var site = cut >= 0 ? t[(cut + len)..].Trim() : t;
        // Long or empty tail is probably part of a page title, not a site name.
        return site.Length is > 0 and <= 30 ? site : "Other";
    }
}
