using System.Text.Json;
using System.Text.Json.Nodes;

namespace DaiseyTracker;

/// <summary>Builds summary/YYYY-MM-DD.json: active minutes per app, browser site and hour, plus audio. No window titles.</summary>
static class Summary
{
    public const string AfkData = "{\"status\":\"afk\"}";
    public const string NotAfkData = "{\"status\":\"not-afk\"}";

    static long Ms(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    public static string Write(Store store, DateTime day)
    {
        day = day.Date;
        var node = JsonSerializer.SerializeToNode(Compute(store, day))!.AsObject();

        // Icons for the apps listed (exe icons, not window content).
        var icons = new JsonObject();
        foreach (var key in new[] { "appMinutes", "audioMinutesByProcess" })
            foreach (var (app, _) in node[key]!.AsObject())
                if (!icons.ContainsKey(app) && Icons.DataUri(app) is { } uri) icons[app] = uri;
        node["icons"] = icons;

        var json = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        Directory.CreateDirectory(Paths.SummaryDir);
        var path = Path.Combine(Paths.SummaryDir, $"{day:yyyy-MM-dd}.json");
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

    public static object Compute(Store store, DateTime day)
    {
        day = day.Date;
        long start = Ms(day), end = Ms(day.AddDays(1)); // local midnight to local midnight

        var apps = new Dictionary<string, double>();
        var hourly = new double[24];
        var perMinute = new Dictionary<string, double>?[1500]; // minute of day -> app -> seconds (1500 covers 25h DST days)
        var sites = new Dictionary<string, double>(); // browser time by site guessed from tab title
        var details = new Dictionary<string, Dictionary<string, double>>(); // app -> project/file -> minutes
        var audio = new Dictionary<string, double>();

        // AFK intervals (backdated to last input by the tracker) are cut out of window time.
        var afk = store.Query("afk", start, end)
            .Where(e => e.Data == AfkData).Select(e => (S: e.Ts, E: e.End)).ToList();

        foreach (var ev in store.Query("window", start, end))
        {
            var app = ReadString(ev.Data, "app");
            var title = ReadString(ev.Data, "title");
            var site = Sites.IsBrowser(app) ? Sites.FromTitle(title) : null;
            var detail = Details.FromTitle(app, title);
            foreach (var seg in Subtract((Math.Max(ev.Ts, start), Math.Min(ev.End, end)), afk))
            {
                if (seg.E <= seg.S) continue;
                apps[app] = apps.GetValueOrDefault(app) + (seg.E - seg.S) / 60000.0;
                if (site is not null) sites[site] = sites.GetValueOrDefault(site) + (seg.E - seg.S) / 60000.0;
                if (detail is not null)
                {
                    if (!details.TryGetValue(app, out var dm)) details[app] = dm = new();
                    dm[detail] = dm.GetValueOrDefault(detail) + (seg.E - seg.S) / 60000.0;
                }
                // Seconds per app per minute, for the timeline.
                for (long t = seg.S; t < seg.E;)
                {
                    int mi = (int)Math.Clamp((t - start) / 60_000, 0, perMinute.Length - 1);
                    long minEnd = Math.Min(seg.E, start + (mi + 1) * 60_000L);
                    (perMinute[mi] ??= new())[app] = perMinute[mi]!.GetValueOrDefault(app) + (minEnd - t) / 1000.0;
                    t = minEnd;
                }
                // Spread over local hours.
                for (long t = seg.S; t < seg.E;)
                {
                    int h = (int)Math.Clamp((t - start) / 3_600_000, 0, 23);
                    long hourEnd = h == 23 ? seg.E : Math.Min(seg.E, start + (h + 1) * 3_600_000L);
                    hourly[h] += (hourEnd - t) / 60000.0;
                    t = hourEnd;
                }
            }
        }

        // Timeline: each minute goes to the app used most in it (>= 20s active, else idle), merged into runs.
        var timeline = new List<object[]>();
        string? runApp = null; int runStart = 0;
        for (int mi = 0; mi <= perMinute.Length; mi++)
        {
            string? top = null;
            if (mi < perMinute.Length && perMinute[mi] is { } m)
            {
                var best = m.MaxBy(kv => kv.Value);
                if (best.Value >= 20) top = best.Key;
            }
            if (top == runApp) continue;
            if (runApp is not null) timeline.Add(new object[] { runStart, mi, runApp });
            runApp = top; runStart = mi;
        }

        // Audio: separate stream, not part of active time.
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
            activeMinutes = Math.Round(apps.Values.Sum(), 2),
            appMinutes = Sorted(apps),
            siteMinutes = Sorted(sites),
            // Top 6 projects/files per app (names only, guessed from window titles).
            detailMinutes = details.ToDictionary(kv => kv.Key,
                kv => kv.Value.Where(x => x.Value >= 0.5).OrderByDescending(x => x.Value).Take(6)
                        .ToDictionary(x => x.Key, x => Math.Round(x.Value, 2))),
            timeline, // [startMinute, endMinute, app], minutes from local midnight
            hourlyMinutes = hourly.Select(v => Math.Round(v, 1)).ToArray(),
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

    static string ReadString(string json, string key)
    {
        using var d = JsonDocument.Parse(json);
        return d.RootElement.TryGetProperty(key, out var v) ? v.GetString() ?? "" : "";
    }
}
