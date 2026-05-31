namespace GameTimeTracker;

public sealed class TrackerState
{
    public List<GameProfile> Games { get; set; } = [];
    public List<PlayInterval> Intervals { get; set; } = [];
    public List<CapacityRule> CapacityRules { get; set; } = [];
}

public sealed class GameProfile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string DisplayName { get; set; } = "New game";
    public List<string> ProcessNames { get; set; } = [];
    public string? ExecutablePath { get; set; }
    public string? IconPath { get; set; }
    public bool IsEnabled { get; set; } = true;
}

public sealed class PlayInterval
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid GameId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public IntervalSource Source { get; set; } = IntervalSource.ProcessTracker;
    public string? Note { get; set; }

    public TimeSpan Duration(DateTimeOffset now)
    {
        var end = EndedAt ?? now;
        return end > StartedAt ? end - StartedAt : TimeSpan.Zero;
    }
}

public sealed class CapacityRule
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? GameId { get; set; }
    public CapacityPeriod Period { get; set; } = CapacityPeriod.Day;
    public int AllowedMinutes { get; set; } = 120;
    public bool IsEnabled { get; set; } = true;
}

public enum IntervalSource
{
    ProcessTracker,
    ManualAdjustment,
    Imported
}

public enum CapacityPeriod
{
    Day,
    Week,
    Month
}
