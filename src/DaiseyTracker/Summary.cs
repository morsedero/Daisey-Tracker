using System.Text.Json;

namespace DaiseyTracker;

/// <summary>Builds summary/YYYY-MM-DD.json: active minutes per app and per hour, plus audio. No window titles.</summary>
static class Summary
{
    public const string AfkData = "{\"status\":\"afk\"}";
    public const string NotAfkData = "{\"status\":\"not-afk\"}";

    static long Ms(DateTime local) => new DateTimeOffset(local).ToUnixTimeMilliseconds();

    public static string Write(Store store, DateTime day)
    {
        day = day.Date;
        var json = JsonSerializer.Serialize(Compute(store, day), new JsonSerializerOptions { WriteIndented = true });
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
        var audio = new Dictionary<string, double>();

        // AFK intervals (backdated to last input by the tracker) are cut out of window time.
        var afk = store.Query("afk", start, end)
            .Where(e => e.Data == AfkData).Select(e => (S: e.Ts, E: e.End)).ToList();

        foreach (var ev in store.Query("window", start, end))
        {
            var app = ReadString(ev.Data, "app");
            foreach (var seg in Subtract((Math.Max(ev.Ts, start), Math.Min(ev.End, end)), afk))
            {
                if (seg.E <= seg.S) continue;
                apps[app] = apps.GetValueOrDefault(app) + (seg.E - seg.S) / 60000.0;
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
