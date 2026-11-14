using System.Text.RegularExpressions;

namespace DaiseyTracker;

/// <summary>
/// Guesses the project/file open inside an app from its window title.
/// Most apps title windows "item - App" or "file - project - App": take the part before the app name.
/// </summary>
static class Details
{
    static readonly string[] Seps = { " - ", " — ", " – ", " | " };
    static readonly Regex Marks = new(@"^[●•*\s]+|[*\s]+$");

    public static string? FromTitle(string app, string title)
    {
        if (Sites.IsBrowser(app)) return null; // browsers are grouped by site instead
        var parts = Split(title);
        if (parts.Count < 2) return null;     // no "item - App" shape: nothing to say

        // VS Code / Cursor: "file - project - Visual Studio Code" -> project.
        var a = app.ToLowerInvariant();
        string pick = (a is "code.exe" or "cursor.exe" or "windsurf.exe") && parts.Count >= 3
            ? parts[^2]
            : parts[^2] is { Length: > 0 } p ? p : parts[0];

        pick = Marks.Replace(pick, "");
        if (pick.Contains('\\') || pick.Contains('/')) pick = Path.GetFileName(pick.TrimEnd('\\', '/')); // paths -> name only
        if (pick.Length is 0 or > 40) return null;
        if (pick.Equals(app.Replace(".exe", "", StringComparison.OrdinalIgnoreCase), StringComparison.OrdinalIgnoreCase)) return null;
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
