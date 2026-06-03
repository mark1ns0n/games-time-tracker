namespace GameTimeTracker;

internal static class AppPaths
{
    public static string AppDataDirectory
    {
        get
        {
            var basePath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(basePath, "GameTimeTracker");
        }
    }

    public static string DataFilePath => Path.Combine(AppDataDirectory, "tracker-data.json");
    public static string IconCacheDirectory => Path.Combine(AppDataDirectory, "icons");
}
