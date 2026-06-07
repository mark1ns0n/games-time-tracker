using Microsoft.Win32;

namespace GameTimeTracker;

internal sealed class SteamLibraryImportService
{
    private static readonly string[] ManifestKeys = ["appid", "name", "installdir"];
    private static readonly string[] ExcludedExecutableTokens =
    [
        "crash", "diagnostic", "dotnet", "helper", "install", "launcherhelper",
        "redist", "setup", "unins", "unitycrashhandler", "vc_redist", "vcredist"
    ];

    public IReadOnlyList<SteamGameCandidate> FindCandidates(IEnumerable<GameProfile> existingGames)
    {
        var knownProcesses = existingGames
            .SelectMany(game => game.ProcessNames)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownExecutablePaths = existingGames
            .Select(game => game.ExecutablePath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.GetFullPath(Environment.ExpandEnvironmentVariables(path!)))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return FindSteamLibraries()
            .SelectMany(ReadLibraryManifests)
            .GroupBy(candidate => candidate.AppId)
            .Select(group => group.First())
            .Where(candidate => !IsKnown(candidate, knownProcesses, knownExecutablePaths))
            .OrderBy(candidate => candidate.Name)
            .ToList();
    }

    private static bool IsKnown(
        SteamGameCandidate candidate,
        HashSet<string> knownProcesses,
        HashSet<string> knownExecutablePaths)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ProcessName) && knownProcesses.Contains(candidate.ProcessName))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(candidate.ExecutablePath))
        {
            var fullPath = Path.GetFullPath(candidate.ExecutablePath);
            return knownExecutablePaths.Contains(fullPath);
        }

        return false;
    }

    private static IReadOnlyList<string> FindSteamLibraries()
    {
        var steamRoots = FindSteamRoots();
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in steamRoots)
        {
            AddLibrary(libraries, root);

            var libraryFolders = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(libraryFolders))
            {
                continue;
            }

            foreach (var path in ReadLibraryFolderPaths(libraryFolders))
            {
                AddLibrary(libraries, path);
            }
        }

        return libraries.OrderBy(path => path).ToList();
    }

    private static IReadOnlyList<string> FindSteamRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddRegistrySteamRoot(roots, Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"));
        AddRegistrySteamRoot(roots, Registry.LocalMachine.OpenSubKey(@"Software\Valve\Steam"));
        AddRegistrySteamRoot(roots, Registry.LocalMachine.OpenSubKey(@"Software\WOW6432Node\Valve\Steam"));

        var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
        {
            roots.Add(Path.Combine(programFilesX86, "Steam"));
        }

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
        {
            roots.Add(Path.Combine(programFiles, "Steam"));
        }

        return roots
            .Select(Environment.ExpandEnvironmentVariables)
            .Where(path => Directory.Exists(Path.Combine(path, "steamapps")))
            .Select(Path.GetFullPath)
            .ToList();
    }

    private static void AddRegistrySteamRoot(HashSet<string> roots, RegistryKey? key)
    {
        using (key)
        {
            if (key is null)
            {
                return;
            }

            AddRegistryPath(roots, key.GetValue("SteamPath") as string);
            AddRegistryPath(roots, key.GetValue("InstallPath") as string);
        }
    }

    private static void AddRegistryPath(HashSet<string> roots, string? path)
    {
        if (!string.IsNullOrWhiteSpace(path))
        {
            roots.Add(path.Replace('/', Path.DirectorySeparatorChar));
        }
    }

    private static void AddLibrary(HashSet<string> libraries, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        var normalized = Environment.ExpandEnvironmentVariables(path.Trim())
            .Replace('/', Path.DirectorySeparatorChar);
        if (Directory.Exists(Path.Combine(normalized, "steamapps")))
        {
            libraries.Add(Path.GetFullPath(normalized));
        }
    }

    private static IEnumerable<string> ReadLibraryFolderPaths(string libraryFoldersPath)
    {
        foreach (var line in File.ReadLines(libraryFoldersPath))
        {
            var values = ParseQuotedValues(line);
            if (values.Count != 2)
            {
                continue;
            }

            var key = values[0];
            var value = values[1].Replace(@"\\", @"\");
            if (key.Equals("path", StringComparison.OrdinalIgnoreCase))
            {
                yield return value;
            }
            else if (int.TryParse(key, out _) && Directory.Exists(Path.Combine(value, "steamapps")))
            {
                yield return value;
            }
        }
    }

    private static IEnumerable<SteamGameCandidate> ReadLibraryManifests(string libraryPath)
    {
        var steamApps = Path.Combine(libraryPath, "steamapps");
        if (!Directory.Exists(steamApps))
        {
            yield break;
        }

        foreach (var manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
        {
            var values = ReadValveKeyValues(manifest).FirstOrDefault();
            if (values is null ||
                !values.TryGetValue("appid", out var appId) ||
                !values.TryGetValue("name", out var name) ||
                !values.TryGetValue("installdir", out var installDirectoryName))
            {
                continue;
            }

            var installDirectory = Path.Combine(steamApps, "common", installDirectoryName);
            if (!Directory.Exists(installDirectory))
            {
                continue;
            }

            var executablePath = GuessExecutablePath(installDirectory, name);
            yield return new SteamGameCandidate(
                appId,
                name,
                libraryPath,
                installDirectory,
                executablePath,
                executablePath is null ? "" : Path.GetFileName(executablePath));
        }
    }

    private static IReadOnlyList<Dictionary<string, string>> ReadValveKeyValues(string path)
    {
        var blocks = new List<Dictionary<string, string>>();
        var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var line in File.ReadLines(path))
        {
            var values = ParseQuotedValues(line);
            if (values.Count != 2)
            {
                continue;
            }

            if (ManifestKeys.Contains(values[0], StringComparer.OrdinalIgnoreCase) || values[0] == "path" || int.TryParse(values[0], out _))
            {
                current[values[0]] = values[1].Replace(@"\\", @"\");
            }

            if (current.ContainsKey("appid") && current.ContainsKey("name") && current.ContainsKey("installdir"))
            {
                blocks.Add(current);
                current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }
        }

        if (current.Count > 0)
        {
            blocks.Add(current);
        }

        return blocks;
    }

    private static List<string> ParseQuotedValues(string line)
    {
        var values = new List<string>();
        var start = -1;
        var escaped = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (start >= 0)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (character == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (character == '"')
                {
                    values.Add(line[(start + 1)..i]);
                    start = -1;
                }

                continue;
            }

            if (character == '"')
            {
                start = i;
            }
        }

        return values;
    }

    private static string? GuessExecutablePath(string installDirectory, string gameName)
    {
        try
        {
            var options = new EnumerationOptions
            {
                IgnoreInaccessible = true,
                RecurseSubdirectories = true
            };

            return Directory
                .EnumerateFiles(installDirectory, "*.exe", options)
                .Take(300)
                .Select(path => new ExecutableCandidate(path, ScoreExecutable(installDirectory, gameName, path)))
                .Where(candidate => candidate.Score > int.MinValue)
                .OrderByDescending(candidate => candidate.Score)
                .ThenBy(candidate => candidate.Path.Length)
                .Select(candidate => candidate.Path)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private static int ScoreExecutable(string installDirectory, string gameName, string path)
    {
        var relativePath = Path.GetRelativePath(installDirectory, path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var normalizedFile = NormalizeForScore(fileName);
        var normalizedName = NormalizeForScore(gameName);
        var normalizedInstall = NormalizeForScore(Path.GetFileName(installDirectory));
        var normalizedRelative = NormalizeForScore(relativePath);

        if (ExcludedExecutableTokens.Any(token => normalizedRelative.Contains(token, StringComparison.OrdinalIgnoreCase)))
        {
            return int.MinValue;
        }

        var score = 0;
        if (normalizedFile == normalizedName || normalizedFile == normalizedInstall)
        {
            score += 100;
        }

        if (normalizedName.Contains(normalizedFile, StringComparison.OrdinalIgnoreCase) ||
            normalizedFile.Contains(normalizedName, StringComparison.OrdinalIgnoreCase))
        {
            score += 50;
        }

        if (normalizedInstall.Contains(normalizedFile, StringComparison.OrdinalIgnoreCase) ||
            normalizedFile.Contains(normalizedInstall, StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        if (!relativePath.Contains(Path.DirectorySeparatorChar))
        {
            score += 25;
        }

        if (normalizedRelative.Contains("bin", StringComparison.OrdinalIgnoreCase) ||
            normalizedRelative.Contains("win64", StringComparison.OrdinalIgnoreCase) ||
            normalizedRelative.Contains("x64", StringComparison.OrdinalIgnoreCase))
        {
            score += 10;
        }

        return score;
    }

    private static string NormalizeForScore(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private sealed record ExecutableCandidate(string Path, int Score);
}

internal sealed record SteamGameCandidate(
    string AppId,
    string Name,
    string LibraryPath,
    string InstallDirectory,
    string? ExecutablePath,
    string ProcessName);
