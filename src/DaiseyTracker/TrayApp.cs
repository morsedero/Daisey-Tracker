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
    readonly ToolStripMenuItem _link = new("Connect to Daisey");
    CancellationTokenSource? _connecting;
    readonly Timer _midnight = new() { Interval = 30_000 };
    readonly Timer _update = new() { Interval = 60_000 };
    DateTime _day = Summary.Today;
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
        menu.Items.Add("Open dashboard", null, (_, _) => OpenDashboard());
        menu.Items.Add(_link);
        _link.Click += (_, _) => LinkClicked();
        PaintLink();
        menu.Items.Add(_pause);
        menu.Items.Add("Export today", null, (_, _) => Export());
        menu.Items.Add("Open data folder", null, (_, _) => Process.Start("explorer.exe", $"\"{Paths.Data}\""));
        menu.Items.Add(_auto);
        menu.Items.Add("Check for updates", null, async (_, _) => await CheckUpdate(manual: true));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Quit", null, (_, _) => Quit());

        _tray = new NotifyIcon { Icon = AppIcon, Text = Title, ContextMenuStrip = menu, Visible = true };

        _tray.DoubleClick += (_, _) => OpenDashboard();

        // Day rollover (also catches wake-from-sleep past midnight): write the finished day.
        _midnight.Tick += (_, _) =>
        {
            if (Summary.Today != _day)
            {
                WriteSummary(_day, quiet: true, final: true);
                _day = Summary.Today;
                Prune();
            }
            // Keep the cloud dashboard fresh: export today every 1 min.
            if (DateTime.Now - _lastExport >= TimeSpan.FromMinutes(1))
            {
                _lastExport = DateTime.Now;
                WriteSummary(Summary.Today, quiet: true);
            }
        };
        _midnight.Start();
        WriteSummary(_day.AddDays(-1), quiet: true, final: true); // yesterday may have missed its final write (PC off at day end)
        Prune();
        // Not linked yet: open Daisey's Approve page right away, once per PC (also after the update that brings this).
        if (!DaiseyLink.Linked && !DaiseyLink.Asked) StartConnect();

        // Auto-update: check every minute (cheap conditional request).
        _update.Tick += async (_, _) => { _update.Interval = 60_000; await CheckUpdate(manual: false); };
        _update.Interval = 60_000;
        _update.Start();

        SystemEvents.SessionEnding += (_, _) => Shutdown();
        _tracker.Resume();
        if (openDashboard && _connecting is null) OpenDashboard();
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

    static readonly Icon AppIcon = (Environment.ProcessPath is { } p ? Icon.ExtractAssociatedIcon(p) : null) ?? SystemIcons.Application;

    void TogglePause()
    {
        if (_tracker.Paused) _tracker.Resume(); else _tracker.Pause();
        _pause.Text = _tracker.Paused ? "Resume" : "Pause";
        _tray.Icon = _tracker.Paused ? SystemIcons.Warning : AppIcon;
        _tray.Text = _tracker.Paused ? Title + " (paused)" : Title;
    }

    /// <summary>Raw events older than 90 days go; the daily summary files stay forever.</summary>
    void Prune()
    {
        try { _store.Prune(90); } catch (Exception ex) { Paths.LogError("prune", ex); }
    }

    void Export() => WriteSummary(Summary.Today, quiet: false);

    void WriteSummary(DateTime day, bool quiet, bool final = false)
    {
        try
        {
            var node = Summary.Write(_store, day);
            if (DaiseyLink.Linked) _ = Send(node, final);
            if (!quiet) _tray.ShowBalloonTip(3000, "Daisey Tracker", $"Saved {Summary.PathFor(day)}", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            Paths.LogError("summary", ex);
            if (!quiet) _tray.ShowBalloonTip(3000, "Daisey Tracker", "Export failed, see error.log", ToolTipIcon.Error);
        }
    }

    // ---- Daisey link ----
    void OpenDashboard()
    {
        if (DaiseyLink.Linked) Process.Start(new ProcessStartInfo(DaiseyLink.AppUrl + "?open=activity") { UseShellExecute = true });
        else _dash.Open();
    }

    void PaintLink()
    {
        _link.Text = _connecting is not null ? "Connecting to Daisey… (click to cancel)"
            : DaiseyLink.Linked ? "Connected to Daisey (click to disconnect)" : "Connect to Daisey";
        _link.Checked = DaiseyLink.Linked;
    }

    void LinkClicked()
    {
        if (_connecting is not null) { _connecting.Cancel(); return; }
        if (!DaiseyLink.Linked) { StartConnect(); return; }
        if (MessageBox.Show("Stop sending this PC's activity to Daisey?\nTo delete what Daisey has, use Settings → Computer → Disconnect in Daisey.",
                Title, MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        DaiseyLink.Forget();
        PaintLink();
    }

    async void StartConnect()
    {
        if (_connecting is not null) return;
        _connecting = new CancellationTokenSource();
        PaintLink();
        try
        {
            if (await DaiseyLink.Connect(_connecting.Token))
            {
                _tray.ShowBalloonTip(4000, Title, "Connected to Daisey. Your activity shows there under PC activity.", ToolTipIcon.Info);
                _lastExport = DateTime.MinValue; // send now
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Paths.LogError("daisey connect", ex);
            _tray.ShowBalloonTip(4000, Title, "Couldn't reach Daisey. Try Connect to Daisey again later.", ToolTipIcon.Warning);
        }
        finally { _connecting.Dispose(); _connecting = null; if (!_closed) PaintLink(); }
    }

    async Task Send(System.Text.Json.Nodes.JsonObject node, bool final)
    {
        if (await DaiseyLink.Upload(node, final) || _closed) return;
        PaintLink();
        _tray.ShowBalloonTip(4000, Title, "This PC was disconnected from Daisey. Choose Connect to Daisey to link it again.", ToolTipIcon.Warning);
    }

    void Shutdown()
    {
        if (_closed) return;
        _closed = true;
        _tracker.Pause();
        _update.Stop();
        _midnight.Stop();
        WriteSummary(Summary.Today, quiet: true);
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
