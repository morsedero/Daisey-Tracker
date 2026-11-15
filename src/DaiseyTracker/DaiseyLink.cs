using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace DaiseyTracker;

/// <summary>
/// Link to the user's Daisey account (morsedero.com/daisey/now). Connect opens Daisey in the browser,
/// the user clicks Approve, and this PC gets a device token; after that, summaries go to Daisey.
/// The token sits in the data folder (only this Windows user can read it).
/// </summary>
static class DaiseyLink
{
    const string Site = "https://morsedero.com";
    const string Api = Site + "/.netlify/functions/daisey-now-tracker";
    public const string AppUrl = Site + "/daisey/now/";
    static readonly string TokenFile = Path.Combine(Paths.Data, "daisey-link.txt");
    static readonly string AskedFile = Path.Combine(Paths.Data, "daisey-link-asked.txt");
    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    static string? _token = Read(TokenFile);
    static string? _lastSent;  // hash of the last summary sent, so an unchanged day isn't sent again
    static string? _iconsSent; // hash of the icons Daisey already has

    public static bool Linked => _token is not null;
    /// <summary>True once Connect was offered on this PC, so a first run asks only once.</summary>
    public static bool Asked => File.Exists(AskedFile);

    static string? Read(string path)
    {
        try { var s = File.Exists(path) ? File.ReadAllText(path).Trim() : ""; return s.Length > 0 ? s : null; } catch { return null; }
    }

    /// <summary>Opens the Approve page and waits up to 15 min for it. True = linked.</summary>
    public static async Task<bool> Connect(CancellationToken stop)
    {
        var start = await Post(new { action = "start", device = Summary.Device }, null, stop);
        var req = start?["req"]?.GetValue<string>() ?? throw new InvalidOperationException("no request id");
        Process.Start(new ProcessStartInfo(AppUrl + "?tracker=" + Uri.EscapeDataString(req)) { UseShellExecute = true });
        try { File.WriteAllText(AskedFile, DateTime.Now.ToString("s")); } catch { } // asked: a first run won't open the page again
        for (var until = DateTime.Now.AddMinutes(15); DateTime.Now < until;)
        {
            await Task.Delay(3000, stop);
            var p = await Post(new { action = "poll", req }, null, stop);
            var status = p?["status"]?.GetValue<string>();
            if (status == "approved" && p?["token"]?.GetValue<string>() is { } token)
            {
                File.WriteAllText(TokenFile, token);
                _token = token; _lastSent = _iconsSent = null;
                return true;
            }
            if (status == "gone") return false;
        }
        return false;
    }

    public static void Forget()
    {
        _token = null;
        try { File.Delete(TokenFile); } catch { }
    }

    /// <summary>Sends one day's summary if it changed. False = Daisey no longer knows this PC (link dropped).</summary>
    public static async Task<bool> Upload(JsonObject summary, bool force = false)
    {
        if (_token is null) return true;
        var day = summary.DeepClone().AsObject();
        var icons = day["icons"] as JsonObject;
        day.Remove("icons"); day.Remove("updatedAt"); day.Remove("audioMinutesByProcess");
        var dayJson = day.ToJsonString();
        var hash = Hash(dayJson);
        var iconsJson = icons?.ToJsonString() ?? "{}";
        var iconsHash = Hash(iconsJson);
        // At most every 2 min; unchanged, every 5 min, so Daisey still knows the tracker is running.
        if (!force && DateTime.Now - _lastAt < TimeSpan.FromMinutes(hash == _lastSent ? 5 : 2)) return true;
        var body = new JsonObject { ["action"] = "upload", ["day"] = JsonNode.Parse(dayJson) };
        if (iconsHash != _iconsSent) body["icons"] = JsonNode.Parse(iconsJson);
        try
        {
            using var res = await Http.SendAsync(Request(body, _token));
            if (res.StatusCode == HttpStatusCode.Unauthorized) { Forget(); return false; }
            res.EnsureSuccessStatusCode();
            _lastSent = hash; _lastAt = DateTime.Now; _iconsSent = iconsHash;
        }
        catch (Exception ex) { Paths.LogError("daisey upload", ex); }
        return true;
    }
    static DateTime _lastAt;

    static HttpRequestMessage Request(object body, string? token)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, Api) { Content = JsonContent.Create(body) };
        if (token is not null) req.Headers.Authorization = new("Bearer", token);
        req.Headers.UserAgent.ParseAdd($"DaiseyTracker/{Updater.Current.ToString(3)}");
        return req;
    }

    static async Task<JsonNode?> Post(object body, string? token, CancellationToken stop)
    {
        using var res = await Http.SendAsync(Request(body, token), stop);
        res.EnsureSuccessStatusCode();
        return JsonNode.Parse(await res.Content.ReadAsStringAsync(stop));
    }

    static string Hash(string s) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(s)));
}
