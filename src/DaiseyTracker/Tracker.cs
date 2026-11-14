using System.Text.Json;
using Timer = System.Windows.Forms.Timer;

namespace DaiseyTracker;

/// <summary>1s poll: window, afk, audio streams. Runs on the UI thread.</summary>
sealed class Tracker : IDisposable
{
    const long AfkMs = 3 * 60 * 1000;

    readonly Store _store;
    readonly Timer _timer = new() { Interval = 1000 };
    bool _afk;

    public Tracker(Store store)
    {
        _store = store;
        _timer.Tick += (_, _) => Tick();
    }

    public bool Paused => !_timer.Enabled;

    public void Pause() => _timer.Stop();

    public void Resume()
    {
        _afk = false;
        _timer.Start();
    }

    void Tick()
    {
        try
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            long idle = Native.IdleMs();
            bool afk = idle >= AfkMs;

            // afk stream. On entering AFK, backdate to the last input (like aw-watcher-afk).
            if (afk)
            {
                if (!_afk) _store.Heartbeat("afk", now - idle, Summary.AfkData);
                _store.Heartbeat("afk", now, Summary.AfkData, pulse: idle / 1000.0 + 5);
            }
            else
            {
                _store.Heartbeat("afk", now, Summary.NotAfkData);
            }
            _afk = afk;

            // window stream: only while not AFK.
            if (!afk && Native.Foreground() is { } w)
                _store.Heartbeat("window", now, JsonSerializer.Serialize(new { app = w.App, title = w.Title }));

            // audio stream: independent of AFK (background music counts).
            foreach (var app in AudioProbe.Playing())
                _store.Heartbeat("audio", now, JsonSerializer.Serialize(new { app }));
        }
        catch (Exception ex)
        {
            Paths.LogError("tick", ex);
        }
    }

    public void Dispose() => _timer.Dispose();
}
