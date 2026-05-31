using System.Diagnostics;

var appDll = FindGameTimeTrackerDll();

if (!File.Exists(appDll))
{
    Environment.ExitCode = 2;
    return;
}

var startInfo = new ProcessStartInfo
{
    FileName = "dotnet.exe",
    Arguments = Quote(appDll),
    UseShellExecute = false,
    CreateNoWindow = true
};

Process.Start(startInfo);

static string FindGameTimeTrackerDll()
{
    var current = new DirectoryInfo(AppContext.BaseDirectory);

    while (current is not null)
    {
        var candidate = Path.Combine(
            current.FullName,
            "src",
            "GameTimeTracker",
            "bin",
            "Debug",
            "net10.0-windows",
            "GameTimeTracker.dll");

        if (File.Exists(candidate))
        {
            return candidate;
        }

        current = current.Parent;
    }

    return @"C:\projects\GameTimeTracker\src\GameTimeTracker\bin\Debug\net10.0-windows\GameTimeTracker.dll";
}

static string Quote(string value) => "\"" + value.Replace("\"", "\\\"") + "\"";
