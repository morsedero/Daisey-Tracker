using NAudio.CoreAudioApi;

namespace DaiseyTracker;

static class AudioProbe
{
    /// <summary>Process names with an audio session whose peak is above 0 right now.</summary>
    public static HashSet<string> Playing()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var en = new MMDeviceEnumerator();
            foreach (var dev in en.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                using (dev)
                {
                    var sessions = dev.AudioSessionManager.Sessions;
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        var s = sessions[i];
                        if (s.IsSystemSoundsSession) continue;
                        if (s.AudioMeterInformation.MasterPeakValue <= 0f) continue;
                        var pid = s.GetProcessID;
                        if (pid == 0) continue;
                        names.Add(Native.ProcessName(pid));
                    }
                }
            }
        }
        catch { /* device went away mid-poll; report what we have */ }
        return names;
    }
}
