using System.Diagnostics;
using Microsoft.Win32;
using Timer = System.Windows.Forms.Timer;

namespace DaiseyTracker;

sealed class TrayApp : ApplicationContext
{
    readonly NotifyIcon _tray;
    readonly Store _store;
    readonly Tracker _tracker;
    readonly ToolStripMenuItem _pause = new("Pause");
    readonly ToolStripMenuItem _auto = new("Start with Windows");
    readonly Timer _midnight = new() { Interval = 30_000 };
    readonly Timer _update = new() { Interval = 6 * 3600 * 1000 };
    DateTime _day = DateTime.Today;
    DateTime _lastExport = DateTime.MinValue; // export soon after start
    bool _updating;
    bool _closed;

    readonly Dashboard _dash;

    public TrayApp(bool openDashboard)
    {
        Paths.Init();
        _store = new Store(Paths.Db);
        _tracker = new Tracker(_store);
        _dash = new Dashboard(_store);

        _pause.Click += (_, _) => TogglePause();
        _auto.Checked = Autostart.IsOn;
        _auto.Click += (_, _) => { Autostart.Set(!_auto.Checked); _auto.Checked = Autostart.IsOn; };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, (_, _) => _dash.Open());
        menu.Items.Add(_pause);
        menu.Items.Add("Export today", null, (_, _) => Export());
        menu.Items.Add("Open data folder", null, (_, _) => Process.Start("explorer.exe", $"\"{Paths.Data}\""));
        menu.Items.Add(_auto);
        menu.Items.Add("Check for updates", null, async (_, _) => await CheckUpdate(manual: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = Title, ContextMenuStrip = menu, Visible = true };

        _tray.DoubleClick += (_, _) => _dash.Open();

        // Day rollover (also catches wake-from-sleep past midnight): write the finished day.
        _midnight.Tick += (_, _) =>
        {
            if (DateTime.Today != _day)
            {
                WriteSummary(_day, quiet: true);
                _day = DateTime.Today;
            }
            // Keep the cloud dashboard fresh: export today every 2 min.
            if (DateTime.Now - _lastExport >= TimeSpan.FromMinutes(2))
            {
                _lastExport = DateTime.Now;
                WriteSummary(DateTime.Today, quiet: true);
            }
        };
        _midnight.Start();

        // Auto-update: first check a minute after start, then every 6 hours.
        _update.Tick += async (_, _) => { _update.Interval = 6 * 3600 * 1000; await CheckUpdate(manual: false); };
        _update.Interval = 60_000;
        _update.Start();

        SystemEvents.SessionEnding += (_, _) => Shutdown();
        _tracker.Resume();
        if (openDashboard) _dash.Open();
    }

    static string Title => Updater.Enabled ? $"Daisey Tracker v{Updater.Current.ToString(3)}" : "Daisey Tracker (dev)";

    async Task CheckUpdate(bool manual)
    {
        if (_updating || _closed) return;
        _updating = true;
        try
        {
            if (await Updater.TryUpdate())
            {
                Updater.StartNew();
                Quit();
            }
            else if (manual)
                _tray.ShowBalloonTip(3000, "Daisey Tracker", Updater.Enabled ? "You have the latest version." : "Dev build: updates are off.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Paths.LogError("update", ex);
            if (manual) _tray.ShowBalloonTip(5000, "Daisey Tracker",
                ex is Updater.MovedException ? ex.Message : "Update check failed. The error was saved for Claude to read.", ToolTipIcon.Warning);
        }
        finally { _updating = false; }
    }

    void TogglePause()
    {
        if (_tracker.Paused) _tracker.Resume(); else _tracker.Pause();
        _pause.Text = _tracker.Paused ? "Resume" : "Pause";
        _tray.Icon = _tracker.Paused ? SystemIcons.Warning : SystemIcons.Application;
        _tray.Text = _tracker.Paused ? Title + " (paused)" : Title;
    }

    void Export() => WriteSummary(DateTime.Today, quiet: false);

    void WriteSummary(DateTime day, bool quiet)
    {
        try
        {
            var path = Summary.Write(_store, day);
            if (!quiet) _tray.ShowBalloonTip(3000, "Daisey Tracker", $"Saved {path}", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Paths.LogError("summary", ex);
            if (!quiet) _tray.ShowBalloonTip(3000, "Daisey Tracker", "Export failed, see error.log", ToolTipIcon.Error);
        }
    }

    void Shutdown()
    {
        if (_closed) return;
        _closed = true;
        _tracker.Pause();
        _update.Stop();
        _midnight.Stop();
        WriteSummary(DateTime.Today, quiet: true);
        _dash.Dispose();
        _tray.Visible = false;
        _tray.Dispose();
        _store.Dispose();
    }

    void Quit()
    {
        Shutdown();
        ExitThread();
    }
}
