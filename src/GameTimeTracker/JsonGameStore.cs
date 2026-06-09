using System.Text.Json;

namespace GameTimeTracker;

public sealed class JsonGameStore : IGameStore
{
    private readonly string _path;
    private readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public JsonGameStore(string path)
    {
        _path = path;
    }

    public TrackerState Load()
    {
        if (!File.Exists(_path))
        {
            return new TrackerState();
        }

        var json = File.ReadAllText(_path);
        return JsonSerializer.Deserialize<TrackerState>(json, _jsonOptions) ?? new TrackerState();
    }

    public void Save(TrackerState state)
    {
        var directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var json = JsonSerializer.Serialize(state, _jsonOptions);
        File.WriteAllText(_path, json);
    }
}
