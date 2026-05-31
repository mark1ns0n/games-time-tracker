namespace GameTimeTracker;

public sealed class TrackingEngine : IDisposable
{
    private readonly TrackerState _state;
    private readonly JsonGameStore _store;
    private readonly ProcessSampler _sampler = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };

    public event EventHandler? TrackingChanged;

    public TrackingEngine(TrackerState state, JsonGameStore store)
    {
        _state = state;
        _store = store;
        _timer.Tick += (_, _) => Poll();
    }

    public void Start()
    {
        Poll();
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void Poll()
    {
        var now = DateTimeOffset.Now;
        var running = _sampler.GetRunningProcessNames();
        var changed = false;

        foreach (var game in _state.Games.Where(game => game.IsEnabled))
        {
            var isRunning = game.ProcessNames.Any(process => running.Contains(NormalizeProcessName(process)));
            var openInterval = GetOpenInterval(game.Id);

            if (isRunning && openInterval is null)
            {
                _state.Intervals.Add(new PlayInterval
                {
                    GameId = game.Id,
                    StartedAt = now,
                    Source = IntervalSource.ProcessTracker
                });
                changed = true;
            }
            else if (!isRunning && openInterval is not null)
            {
                openInterval.EndedAt = now;
                changed = true;
            }
        }

        if (changed)
        {
            _store.Save(_state);
            TrackingChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public PlayInterval? GetOpenInterval(Guid gameId)
    {
        return _state.Intervals.LastOrDefault(interval => interval.GameId == gameId && interval.EndedAt is null);
    }

    public TimeSpan GetTotalPlayed(Guid gameId, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var now = DateTimeOffset.Now;
        return _state.Intervals
            .Where(interval => interval.GameId == gameId)
            .Where(interval => from is null || (interval.EndedAt ?? now) >= from.Value)
            .Where(interval => to is null || interval.StartedAt <= to.Value)
            .Select(interval => interval.Duration(now))
            .Aggregate(TimeSpan.Zero, (total, next) => total + next);
    }

    public CapacitySnapshot GetCapacitySnapshot(Guid? gameId, CapacityPeriod period, int allowedMinutes)
    {
        var now = DateTimeOffset.Now;
        var start = period switch
        {
            CapacityPeriod.Day => new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset),
            CapacityPeriod.Week => StartOfWeek(now),
            CapacityPeriod.Month => new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset),
            _ => now
        };

        var used = _state.Intervals
            .Where(interval => gameId is null || interval.GameId == gameId.Value)
            .Where(interval => (interval.EndedAt ?? now) >= start)
            .Select(interval => interval.Duration(now))
            .Aggregate(TimeSpan.Zero, (total, next) => total + next);

        var allowed = TimeSpan.FromMinutes(allowedMinutes);
        return new CapacitySnapshot(allowed, used, allowed - used);
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset now)
    {
        var diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
        var startDate = now.Date.AddDays(-diff);
        return new DateTimeOffset(startDate, now.Offset);
    }

    private static string NormalizeProcessName(string processName)
    {
        var trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".exe";
    }

    public void Dispose() => _timer.Dispose();
}

public readonly record struct CapacitySnapshot(TimeSpan Allowed, TimeSpan Used, TimeSpan Remaining);
