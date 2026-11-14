using System.Text.Json;
using System.Text.RegularExpressions;

namespace DaiseyTracker;

/// <summary>rules.json: ordered regex rules (first match wins) + Waiting title list.</summary>
sealed class RuleSet
{
    public static readonly string[] Categories = { "Deep", "Support", "Other", "Waiting" };
    public const string WaitingProject = "Waiting";

    sealed record Rule(Regex? Proc, Regex? Title, string Project, string Category);

    readonly List<Rule> _rules = new();
    readonly List<Regex> _waiting = new();
    public int GraceSeconds { get; private set; } = 180;

    static Regex Rx(string p) =>
        new(p, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    static string NormCategory(string? c) =>
        Categories.FirstOrDefault(x => x.Equals(c, StringComparison.OrdinalIgnoreCase)) ?? "Other";

    public static RuleSet Load(string path)
    {
        var rs = new RuleSet();
        var opt = new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        using var doc = JsonDocument.Parse(File.ReadAllText(path), opt);
        var root = doc.RootElement;

        if (root.TryGetProperty("waitingGraceSeconds", out var g)) rs.GraceSeconds = g.GetInt32();
        if (root.TryGetProperty("waitingTitles", out var w))
            foreach (var t in w.EnumerateArray()) rs._waiting.Add(Rx(t.GetString()!));

        if (root.TryGetProperty("rules", out var rules))
            foreach (var r in rules.EnumerateArray())
            {
                string? Get(string k) => r.TryGetProperty(k, out var v) ? v.GetString() : null;
                var proc = Get("process");
                var title = Get("title");
                rs._rules.Add(new Rule(
                    proc is null ? null : Rx(proc),
                    title is null ? null : Rx(title),
                    Get("project") ?? "Other",
                    NormCategory(Get("category"))));
            }
        return rs;
    }

    /// <summary>First matching rule; no match = Other/Other with Matched=false.</summary>
    public (string Project, string Category, bool Matched) Classify(string app, string title)
    {
        foreach (var r in _rules)
            if ((r.Proc is null || r.Proc.IsMatch(app)) && (r.Title is null || r.Title.IsMatch(title)))
                return (r.Project, r.Category, true);
        return ("Other", "Other", false);
    }

    /// <summary>Claude window = process claude.exe OR title contains "Claude".</summary>
    public static bool IsClaude(string app, string title) =>
        app.Equals("claude.exe", StringComparison.OrdinalIgnoreCase) ||
        title.Contains("Claude", StringComparison.Ordinal);

    public bool IsWaitingTitle(string title) => _waiting.Any(r => r.IsMatch(title));
}
