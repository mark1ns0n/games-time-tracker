using System.Diagnostics;

namespace GameTimeTracker;

public sealed class ProcessSampler
{
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
}
