using System.Text.Json;

namespace DaiseyTracker;

/// <summary>Builds summary/YYYY-MM-DD.json from stored events. No raw titles in output.</summary>
static class Summary
{
    public const string AfkData = "{\"status\":\"afk\"}";
    public const string NotAfkData = "{\"status\":\"not-afk\"}";

    static long Ms(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    public static string Write(Store store, DateTime day)
    {
        day = day.Date;
        var obj = Compute(store, day);
        Directory.CreateDirectory(Paths.SummaryDir);
        var path = Path.Combine(Paths.SummaryDir, $"{day:yyyy-MM-dd}.json");
        var json = JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json);

        // Mirror to the synced cloud folder so the claude.ai dashboard can read it.
        if (Paths.SyncDir() is { } sync)
        {
            try
            {
                Directory.CreateDirectory(sync);
                File.WriteAllText(Path.Combine(sync, $"daisey-{day:yyyy-MM-dd}.json"), json);
            }
            catch (Exception ex) { Paths.LogError("sync", ex); }
        }
        return path;
    }

    public static object Compute(Store store, DateTime day) =>
        Build(store, RuleSet.Load(Paths.Rules), day.Date);

    static object Build(Store store, RuleSet rules, DateTime day)
    {
        long start = Ms(day), end = Ms(day.AddDays(1)); // local midnight to local midnight
        long graceMs = rules.GraceSeconds * 1000L;

        var projects = new Dictionary<string, double>();
        var cats = RuleSet.Categories.ToDictionary(c => c, _ => 0.0);
        var audio = new Dictionary<string, double>();

        void Emit(long s, long e, string project, string cat)
        {
            s = Math.Max(s, start); e = Math.Min(e, end);
            if (e <= s) return;
            var min = (e - s) / 60000.0;
            projects[project] = projects.GetValueOrDefault(project) + min;
            cats[cat] += min;
        }

        // AFK intervals (backdated to last input by the tracker) are cut out of window time.
        var afk = store.Query("afk", start - graceMs, end)
            .Where(e => e.Data == AfkData).Select(e => (S: e.Ts, E: e.End)).ToList();

        // Window events; start a bit early so "Claude last active" carries over midnight.
        long? claudeEnd = null;
        foreach (var ev in store.Query("window", start - graceMs, end))
        {
            var (app, title) = ReadWindow(ev.Data);
            bool claude = RuleSet.IsClaude(app, title);
            bool waitTitle = rules.IsWaitingTitle(title);

            foreach (var seg in Subtract((ev.Ts, ev.End), afk))
            {
                long s = seg.S, e = seg.E;
                if (claude)
                {
                    var (p, c) = rules.Classify(app, title);
                    Emit(s, e, p, c);
                    claudeEnd = e;
                    continue;
                }
                if (waitTitle && claudeEnd is long ce)
                {
                    // Waiting only within grace period after Claude was last active.
                    long we = Math.Min(e, ce + graceMs);
                    if (we > s)
                    {
                        Emit(s, we, RuleSet.WaitingProject, "Waiting");
                        s = we;
                    }
                }
                if (e > s)
                {
                    var (p, c) = rules.Classify(app, title);
                    Emit(s, e, p, c);
                }
            }
        }

        // Audio: separate stream, never added to project/category time.
        foreach (var ev in store.Query("audio", start, end))
        {
            long s = Math.Max(ev.Ts, start), e = Math.Min(ev.End, end);
            if (e <= s) continue;
            var app = ReadString(ev.Data, "app");
            audio[app] = audio.GetValueOrDefault(app) + (e - s) / 60000.0;
        }

        static Dictionary<string, double> Sorted(Dictionary<string, double> d) =>
            d.OrderByDescending(kv => kv.Value).ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value, 2));

        return new
        {
            date = day.ToString("yyyy-MM-dd"),
            projectMinutes = Sorted(projects),
            categoryMinutes = Sorted(cats),
            audioMinutesByProcess = Sorted(audio),
        };
    }

    static List<(long S, long E)> Subtract((long S, long E) seg, List<(long S, long E)> holes)
    {
        var pieces = new List<(long S, long E)> { seg };
        foreach (var h in holes)
        {
            var next = new List<(long S, long E)>();
            foreach (var p in pieces)
            {
                if (h.E <= p.S || h.S >= p.E) { next.Add(p); continue; }
                if (h.S > p.S) next.Add((p.S, h.S));
                if (h.E < p.E) next.Add((h.E, p.E));
            }
            pieces = next;
        }
        return pieces;
    }

    static (string App, string Title) ReadWindow(string json) =>
        (ReadString(json, "app"), ReadString(json, "title"));

    static string ReadString(string json, string key)
    {
        using var d = JsonDocument.Parse(json);
        return d.RootElement.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
    }
}
