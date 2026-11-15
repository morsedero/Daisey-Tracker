using System.Text.RegularExpressions;

namespace DaiseyTracker;

/// <summary>
/// Guesses the project/file open inside an app from its window title.
/// The app's main window names itself ("Budget.xlsx - Excel", "Cubase Pro Project - Song", "file - project - Visual Studio Code"):
/// drop the part with the app's name, the remaining part closest to it is the project.
/// Other windows of the app (mixer, plugins, dialogs) return null and count toward the last project seen.
/// </summary>
static class Details
{
    static readonly string[] Seps = { " - ", " — ", " – ", " | " };
    static readonly Regex Marks = new(@"^[●•*\s]+|[*\s]+$|\s*\[[^\]]*\]\s*$");
    static readonly Regex TrailingDigits = new(@"[\s\d._-]+$");

    // Exe names that don't match the name in their window titles.
    static readonly Dictionary<string, string> TitleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["winword"] = "Word", ["powerpnt"] = "PowerPoint", ["devenv"] = "Visual Studio", ["fl64"] = "FL Studio",
        ["code"] = "Visual Studio Code", ["acrobat"] = "Acrobat", ["afterfx"] = "After Effects",
    };

    public static string? FromTitle(string app, string title)
    {
        if (Sites.IsBrowser(app)) return null; // browsers are grouped by site instead
        var exe = Path.GetFileNameWithoutExtension(app);
        var key = TitleNames.TryGetValue(exe, out var n) ? n : TrailingDigits.Replace(exe, "");
        if (key.Length < 2) return null;

        var parts = Split(title);
        int appAt = parts.FindLastIndex(p => p.Contains(key, StringComparison.OrdinalIgnoreCase));
        if (appAt < 0 || parts.Count < 2) return null; // not the main window

        // Closest part to the app name: before it if there is one ("file - project - App"), else after ("App - project").
        var pick = appAt > 0 ? parts[appAt - 1] : parts[appAt + 1];
        pick = Marks.Replace(pick, "").Trim();
        if (pick.Contains('\\') || pick.Contains('/')) pick = Path.GetFileName(pick.TrimEnd('\\', '/')); // paths -> name only
        pick = Marks.Replace(pick, "").Trim();
        if (pick.Length is 0 or > 50 || pick.Contains(key, StringComparison.OrdinalIgnoreCase)) return null;
        return pick;
    }

    static List<string> Split(string t)
    {
        var parts = new List<string> { t.Trim() };
        foreach (var s in Seps)
            parts = parts.SelectMany(p => p.Split(s)).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        return parts;
    }
}
