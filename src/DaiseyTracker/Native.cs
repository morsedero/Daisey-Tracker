using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace DaiseyTracker;

static class Native
{
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool GetLastInputInfo(ref LASTINPUTINFO info);

    [StructLayout(LayoutKind.Sequential)]
    struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }

    /// <summary>Milliseconds since last keyboard/mouse input.</summary>
    public static long IdleMs()
    {
        var info = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref info)) return 0;
        return unchecked((uint)Environment.TickCount - info.dwTime);
    }

    static uint _pid;
    static string _name = "unknown";

    /// <summary>Foreground window: process name (e.g. "chrome.exe") + title. Null if none.</summary>
    public static (string App, string Title)? Foreground()
    {
        var h = GetForegroundWindow();
        if (h == IntPtr.Zero) return null;

        var sb = new StringBuilder(GetWindowTextLength(h) + 1);
        GetWindowText(h, sb, sb.Capacity);

        GetWindowThreadProcessId(h, out var pid);
        if (pid != _pid)
        {
            _pid = pid;
            _name = ProcessName(pid);
        }
        return (_name, sb.ToString());
    }

    public static string ProcessName(uint pid)
    {
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName + ".exe";
        }
        catch { return "unknown"; }
    }
}
