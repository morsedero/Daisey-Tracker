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
    DateTime _day = DateTime.Today;
    bool _closed;

    public TrayApp()
    {
        Paths.Init();
        _store = new Store(Paths.Db);
        _tracker = new Tracker(_store);

        _pause.Click += (_, _) => TogglePause();
        _auto.Checked = Autostart.IsOn;
        _auto.Click += (_, _) => { Autostart.Set(!_auto.Checked); _auto.Checked = Autostart.IsOn; };

        var menu = new ContextMenuStrip();
        menu.Items.Add(_pause);
        menu.Items.Add("Export today", null, (_, _) => Export());
        menu.Items.Add("Open data folder", null, (_, _) => Process.Start("explorer.exe", $"\"{Paths.Data}\""));
        menu.Items.Add(_auto);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _tray = new NotifyIcon { Icon = SystemIcons.Application, Text = "Daisey Tracker", ContextMenuStrip = menu, Visible = true };

        // Day rollover (also catches wake-from-sleep past midnight): write the finished day.
        _midnight.Tick += (_, _) =>
        {
            if (DateTime.Today == _day) return;
            WriteSummary(_day, quiet: true);
            _day = DateTime.Today;
        };
        _midnight.Start();

        SystemEvents.SessionEnding += (_, _) => Shutdown();
        _tracker.Resume();
    }

    void TogglePause()
    {
        if (_tracker.Paused) _tracker.Resume(); else _tracker.Pause();
        _pause.Text = _tracker.Paused ? "Resume" : "Pause";
        _tray.Icon = _tracker.Paused ? SystemIcons.Warning : SystemIcons.Application;
        _tray.Text = _tracker.Paused ? "Daisey Tracker (paused)" : "Daisey Tracker";
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
        WriteSummary(DateTime.Today, quiet: true);
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
