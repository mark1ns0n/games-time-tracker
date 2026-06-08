using Microsoft.Win32;

namespace GameTimeTracker;

internal sealed class GogLibraryImportService
{
    private static readonly string[] ExcludedExecutableTokens =
    [
        "crash", "diagnostic", "dotnet", "helper", "install", "launcherhelper",
        "redist", "setup", "support", "unins", "unitycrashhandler", "vc_redist", "vcredist"
    ];

    public IReadOnlyList<GogGameCandidate> FindCandidates(IEnumerable<GameProfile> existingGames)
    {
        var knownProcesses = existingGames
            .SelectMany(game => game.ProcessNames)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownExecutablePaths = existingGames
            .Select(game => TryGetFullPath(game.ExecutablePath))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return FindRegistryCandidates()
            .Concat(FindFolderCandidates())
            .GroupBy(GetCandidateKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(candidate => !IsKnown(candidate, knownProcesses, knownExecutablePaths))
            .OrderBy(candidate => candidate.Name)
            .ToList();
    }

    private static bool IsKnown(
        GogGameCandidate candidate,
        HashSet<string> knownProcesses,
        HashSet<string> knownExecutablePaths)
    {
        if (!string.IsNullOrWhiteSpace(candidate.ProcessName) && knownProcesses.Contains(candidate.ProcessName))
        {
            return true;
        }

        var executablePath = TryGetFullPath(candidate.ExecutablePath);
        return executablePath is not null && knownExecutablePaths.Contains(executablePath);
    }

    private static string GetCandidateKey(GogGameCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.SourceId))
        {
            return candidate.SourceId;
        }

        return $"{candidate.Name}|{candidate.InstallDirectory}";
    }

    private static IEnumerable<GogGameCandidate> FindRegistryCandidates()
    {
        foreach (var registryRoot in OpenUninstallRoots())
        {
            using var root = registryRoot;
            if (root is null)
            {
                continue;
            }

            string[] subKeyNames;
            try
            {
                subKeyNames = root.GetSubKeyNames();
            }
            catch
            {
                continue;
            }

            foreach (var subKeyName in subKeyNames)
            {
                var candidate = ReadRegistryCandidate(root, subKeyName);
                if (candidate is not null)
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IEnumerable<RegistryKey?> OpenUninstallRoots()
    {
        yield return SafeOpenSubKey(Registry.CurrentUser, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
        yield return SafeOpenSubKey(Registry.LocalMachine, @"Software\Microsoft\Windows\CurrentVersion\Uninstall");
        yield return SafeOpenSubKey(Registry.LocalMachine, @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall");
    }

    private static RegistryKey? SafeOpenSubKey(RegistryKey root, string name)
    {
        try
        {
            return root.OpenSubKey(name);
        }
        catch
        {
            return null;
        }
    }

    private static GogGameCandidate? ReadRegistryCandidate(RegistryKey root, string subKeyName)
    {
        try
        {
            using var key = root.OpenSubKey(subKeyName);
            if (key is null)
            {
                return null;
            }

            var name = ReadRegistryString(key, "DisplayName");
            var installDirectory = ReadRegistryString(key, "InstallLocation")
                ?? ReadRegistryString(key, "Inno Setup: App Path")
                ?? ReadInstallDirectory(ReadRegistryString(key, "DisplayIcon"))
                ?? ReadInstallDirectory(ReadRegistryString(key, "UninstallString"));
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDirectory))
            {
                return null;
            }

            installDirectory = NormalizePath(installDirectory);
            if (!Directory.Exists(installDirectory) || IsLauncherName(name))
            {
                return null;
            }

            var publisher = ReadRegistryString(key, "Publisher") ?? "";
            var displayIcon = ReadRegistryString(key, "DisplayIcon");
            var uninstallString = ReadRegistryString(key, "UninstallString");
            if (!LooksLikeGogGame(name, publisher, installDirectory, displayIcon, uninstallString))
            {
                return null;
            }

            var executablePath = ResolveExecutablePath(installDirectory, displayIcon)
                ?? GuessExecutablePath(installDirectory, name);
            return new GogGameCandidate(
                subKeyName,
                name,
                "Registry",
                Path.GetFullPath(installDirectory),
                executablePath,
                executablePath is null ? "" : Path.GetFileName(executablePath));
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<GogGameCandidate> FindFolderCandidates()
    {
        foreach (var libraryRoot in FindLibraryRoots())
        {
            IEnumerable<string> directories;
            try
            {
                directories = Directory.EnumerateDirectories(libraryRoot);
            }
            catch
            {
                continue;
            }

            foreach (var installDirectory in directories)
            {
                var candidate = ReadFolderCandidate(installDirectory);
                if (candidate is not null)
                {
                    yield return candidate;
                }
            }
        }
    }

    private static IReadOnlyList<string> FindLibraryRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddLibraryRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "Games");
        AddLibraryRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "GOG Galaxy", "Games");

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch
        {
            drives = [];
        }

        foreach (var drive in drives)
        {
            try
            {
                if (!drive.IsReady || drive.DriveType is not DriveType.Fixed)
                {
                    continue;
                }

                roots.Add(Path.Combine(drive.RootDirectory.FullName, "GOG Games"));
            }
            catch
            {
                continue;
            }
        }

        return roots
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .OrderBy(path => path)
            .ToList();
    }

    private static void AddLibraryRoot(HashSet<string> roots, string? basePath, params string[] segments)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return;
        }

        roots.Add(Path.Combine(new[] { Environment.ExpandEnvironmentVariables(basePath) }.Concat(segments).ToArray()));
    }

    private static GogGameCandidate? ReadFolderCandidate(string installDirectory)
    {
        try
        {
            if (!Directory.Exists(installDirectory) || IsSupportDirectory(installDirectory))
            {
                return null;
            }

            var name = Path.GetFileName(installDirectory);
            var executablePath = GuessExecutablePath(installDirectory, name);
            if (executablePath is null)
            {
                return null;
            }

            return new GogGameCandidate(
                "",
                name,
                "Folder",
                Path.GetFullPath(installDirectory),
                executablePath,
                Path.GetFileName(executablePath));
        }
        catch
        {
            return null;
        }
    }

    private static bool LooksLikeGogGame(
        string name,
        string publisher,
        string installDirectory,
        string? displayIcon,
        string? uninstallString)
    {
        if (publisher.Contains("GOG", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (name.Contains("GOG.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return PathLooksLikeGog(installDirectory) ||
            PathLooksLikeGog(displayIcon) ||
            PathLooksLikeGog(uninstallString);
    }

    private static bool PathLooksLikeGog(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.Contains("GOG Galaxy", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("GOG Games", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("GOG.com", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLauncherName(string name)
    {
        return name.Equals("GOG Galaxy", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("GOG Galaxy", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportDirectory(string path)
    {
        var name = NormalizeForScore(Path.GetFileName(path));
        return name is "redist" or "redistributables" or "support" or "tmp" or "temp";
    }

    private static string? ReadRegistryString(RegistryKey key, string name)
    {
        return key.GetValue(name) as string;
    }

    private static string? ReadInstallDirectory(string? value)
    {
        var executablePath = CleanExecutableValue(value);
        if (string.IsNullOrWhiteSpace(executablePath))
        {
            return null;
        }

        return Path.GetDirectoryName(executablePath);
    }

    private static string? ResolveExecutablePath(string installDirectory, string? value)
    {
        var cleaned = CleanExecutableValue(value);
        if (string.IsNullOrWhiteSpace(cleaned) || !Path.GetExtension(cleaned).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var normalized = NormalizePath(cleaned);
        var candidates = Path.IsPathRooted(normalized)
            ? [normalized]
            : new[] { Path.Combine(installDirectory, normalized) };

        foreach (var candidate in candidates)
        {
            var fullPath = TryGetFullPath(candidate);
            if (fullPath is not null && File.Exists(fullPath) && !IsExcludedExecutablePath(installDirectory, fullPath))
            {
                return fullPath;
            }
        }

        return null;
    }

    private static string? CleanExecutableValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var cleaned = value.Trim();
        if (cleaned.StartsWith('"'))
        {
            var closingQuote = cleaned.IndexOf('"', 1);
            cleaned = closingQuote > 1 ? cleaned[1..closingQuote] : cleaned.Trim('"');
        }
        else
        {
            var executableEnd = cleaned.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
            if (executableEnd >= 0)
            {
                cleaned = cleaned[..(executableEnd + 4)];
            }
            else
            {
                var comma = cleaned.IndexOf(',');
                if (comma >= 0)
                {
                    cleaned = cleaned[..comma];
                }
            }
        }

        return cleaned.Trim().Trim('"');
    }

    private static string NormalizePath(string path)
    {
        return Environment.ExpandEnvironmentVariables(path)
            .Replace('/', Path.DirectorySeparatorChar);
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

        if (IsExcludedExecutablePath(installDirectory, path))
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

    private static bool IsExcludedExecutablePath(string installDirectory, string path)
    {
        var relativePath = Path.GetRelativePath(installDirectory, path);
        var normalizedRelative = NormalizeForScore(relativePath);
        return ExcludedExecutableTokens.Any(token => normalizedRelative.Contains(token, StringComparison.OrdinalIgnoreCase));
    }

    private static string NormalizeForScore(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    private static string? TryGetFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(NormalizePath(path));
        }
        catch
        {
            return null;
        }
    }

    private sealed record ExecutableCandidate(string Path, int Score);
}

internal sealed record GogGameCandidate(
    string SourceId,
    string Name,
    string Source,
    string InstallDirectory,
    string? ExecutablePath,
    string ProcessName);
