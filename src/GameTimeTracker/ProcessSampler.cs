using System.Diagnostics;
using System.Runtime.InteropServices;

namespace GameTimeTracker;

public sealed class ProcessSampler
{
    public string? GetForegroundProcessName()
    {
        var window = GetForegroundWindow();
        if (window == IntPtr.Zero)
        {
            return null;
        }

        _ = GetWindowThreadProcessId(window, out var processId);
        if (processId == 0)
        {
            return null;
        }

        try
        {
            using var process = Process.GetProcessById((int)processId);
            return process.ProcessName + ".exe";
        }
        catch
        {
            return null;
        }
    }

    public HashSet<string> GetRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var process in Process.GetProcesses())
        {
            try
            {
                names.Add(process.ProcessName + ".exe");
            }
            catch
            {
                // Some protected/system processes cannot be inspected. They are irrelevant for game tracking.
            }
            finally
            {
                process.Dispose();
            }
        }

        return names;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
}
