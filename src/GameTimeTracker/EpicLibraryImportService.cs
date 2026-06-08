using System.Text.Json;

namespace GameTimeTracker;

internal sealed class EpicLibraryImportService
{
    private static readonly string[] ExcludedExecutableTokens =
    [
        "crash", "diagnostic", "dotnet", "helper", "install", "launcherhelper",
        "redist", "setup", "unins", "unitycrashhandler", "vc_redist", "vcredist"
    ];

    public IReadOnlyList<EpicGameCandidate> FindCandidates(IEnumerable<GameProfile> existingGames)
    {
        var knownProcesses = existingGames
            .SelectMany(game => game.ProcessNames)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var knownExecutablePaths = existingGames
            .Select(game => TryGetFullPath(game.ExecutablePath))
            .OfType<string>()
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return FindManifestRoots()
            .SelectMany(ReadManifestRoot)
            .GroupBy(GetCandidateKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(candidate => !IsKnown(candidate, knownProcesses, knownExecutablePaths))
            .OrderBy(candidate => candidate.Name)
            .ToList();
    }

    private static bool IsKnown(
        EpicGameCandidate candidate,
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

    private static string GetCandidateKey(EpicGameCandidate candidate)
    {
        if (!string.IsNullOrWhiteSpace(candidate.CatalogItemId))
        {
            return candidate.CatalogItemId;
        }

        if (!string.IsNullOrWhiteSpace(candidate.AppName))
        {
            return candidate.AppName;
        }

        return candidate.InstallDirectory;
    }

    private static IReadOnlyList<string> FindManifestRoots()
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        AddManifestRoot(roots, Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData));
        AddManifestRoot(roots, Environment.GetEnvironmentVariable("ProgramData"));

        return roots
            .Where(Directory.Exists)
            .Select(Path.GetFullPath)
            .OrderBy(path => path)
            .ToList();
    }

    private static void AddManifestRoot(HashSet<string> roots, string? basePath)
    {
        if (string.IsNullOrWhiteSpace(basePath))
        {
            return;
        }

        roots.Add(Path.Combine(
            Environment.ExpandEnvironmentVariables(basePath),
            "Epic",
            "EpicGamesLauncher",
            "Data",
            "Manifests"));
    }

    private static IEnumerable<EpicGameCandidate> ReadManifestRoot(string manifestRoot)
    {
        IEnumerable<string> manifests;
        try
        {
            manifests = Directory.EnumerateFiles(manifestRoot, "*.item");
        }
        catch
        {
            yield break;
        }

        foreach (var manifest in manifests)
        {
            var candidate = ReadManifest(manifest);
            if (candidate is not null)
            {
                yield return candidate;
            }
        }
    }

    private static EpicGameCandidate? ReadManifest(string manifestPath)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;

            var name = ReadString(root, "DisplayName", "AppName");
            var installDirectory = ReadString(root, "InstallLocation", "InstallPath");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(installDirectory))
            {
                return null;
            }

            installDirectory = Environment.ExpandEnvironmentVariables(installDirectory)
                .Replace('/', Path.DirectorySeparatorChar);
            if (!Directory.Exists(installDirectory))
            {
                return null;
            }

            var executablePath = ResolveExecutablePath(installDirectory, ReadString(root, "LaunchExecutable"))
                ?? GuessExecutablePath(installDirectory, name);
            return new EpicGameCandidate(
                ReadString(root, "AppName") ?? "",
                ReadString(root, "CatalogItemId") ?? "",
                name,
                manifestPath,
                Path.GetFullPath(installDirectory),
                executablePath,
                executablePath is null ? "" : Path.GetFileName(executablePath));
        }
        catch (JsonException)
        {
            return null;
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        foreach (var name in names)
        {
            if (root.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                return property.GetString();
            }
        }

        return null;
    }

    private static string? ResolveExecutablePath(string installDirectory, string? launchExecutable)
    {
        var cleaned = CleanExecutableValue(launchExecutable);
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            return null;
        }

        var normalized = Environment.ExpandEnvironmentVariables(cleaned)
            .Replace('/', Path.DirectorySeparatorChar);
        var candidates = Path.IsPathRooted(normalized)
            ? [normalized]
            : new[] { Path.Combine(installDirectory, normalized) };

        foreach (var candidate in candidates)
        {
            var fullPath = TryGetFullPath(candidate);
            if (fullPath is not null && File.Exists(fullPath))
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

        var cleaned = value.Trim().Trim('"');
        var executableEnd = cleaned.IndexOf(".exe", StringComparison.OrdinalIgnoreCase);
        if (executableEnd >= 0)
        {
            cleaned = cleaned[..(executableEnd + 4)];
        }

        return cleaned.Trim().Trim('"');
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

    private static string? TryGetFullPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        }
        catch
        {
            return null;
        }
    }

    private sealed record ExecutableCandidate(string Path, int Score);
}

internal sealed record EpicGameCandidate(
    string AppName,
    string CatalogItemId,
    string Name,
    string ManifestPath,
    string InstallDirectory,
    string? ExecutablePath,
    string ProcessName);
