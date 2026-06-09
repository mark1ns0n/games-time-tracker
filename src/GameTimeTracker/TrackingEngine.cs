namespace GameTimeTracker;

public sealed class TrackingEngine : IDisposable
{
    private readonly TrackerState _state;
    private readonly IGameStore _store;
    private readonly ProcessSampler _sampler = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 5000 };

    public event EventHandler? TrackingChanged;

    public TrackingEngine(TrackerState state, IGameStore store)
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

    public void Stop()
    {
        _timer.Stop();

        var now = DateTimeOffset.Now;
        var changed = false;
        foreach (var interval in _state.Intervals.Where(interval =>
            interval.Source == IntervalSource.ProcessTracker && interval.EndedAt is null))
        {
            var activeInterval = GetOpenActiveInterval(interval);
            if (activeInterval is not null)
            {
                activeInterval.EndedAt = now;
            }

            interval.EndedAt = now;
            changed = true;
        }

        if (changed)
        {
            _store.Save(_state);
        }
    }

    public void Poll()
    {
        var now = DateTimeOffset.Now;
        var running = _sampler.GetRunningProcessNames();
        var foregroundProcess = _sampler.GetForegroundProcessName();
        var changed = false;

        foreach (var game in _state.Games.Where(game => game.IsEnabled))
        {
            var processNames = game.ProcessNames
                .Select(NormalizeProcessName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var isRunning = processNames.Any(running.Contains);
            var isActive = isRunning
                && foregroundProcess is not null
                && processNames.Contains(foregroundProcess);
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
                openInterval = GetOpenInterval(game.Id);
            }
            else if (!isRunning && openInterval is not null)
            {
                openInterval.EndedAt = now;
                changed = true;
            }

            if (openInterval is null)
            {
                continue;
            }

            var openActiveInterval = GetOpenActiveInterval(openInterval);
            if (isActive && openActiveInterval is null)
            {
                openInterval.ActiveIntervals.Add(new ActivePlayInterval { StartedAt = now });
                changed = true;
            }
            else if (!isActive && openActiveInterval is not null)
            {
                openActiveInterval.EndedAt = now;
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

    public bool IsGameActive(Guid gameId)
    {
        var openInterval = GetOpenInterval(gameId);
        return openInterval is not null && GetOpenActiveInterval(openInterval) is not null;
    }

    public TimeSpan GetTotalPlayed(Guid gameId, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var now = DateTimeOffset.Now;
        return _state.Intervals
            .Where(interval => interval.GameId == gameId)
            .Select(interval => GetIntervalDurationWithin(interval, now, from, to))
            .Aggregate(TimeSpan.Zero, (total, next) => total + next);
    }

    public TimeSpan GetTotalActive(Guid gameId, DateTimeOffset? from = null, DateTimeOffset? to = null)
    {
        var now = DateTimeOffset.Now;
        return _state.Intervals
            .Where(interval => interval.GameId == gameId)
            .Select(interval => GetActiveDurationWithin(interval, now, from, to))
            .Aggregate(TimeSpan.Zero, (total, next) => total + next);
    }

    public TimeSpan GetActiveDuration(PlayInterval interval, DateTimeOffset? now = null)
    {
        return GetActiveDurationWithin(interval, now ?? DateTimeOffset.Now, null, null);
    }

    public IReadOnlyList<CapacityRule> GetCapacityRules()
    {
        return _state.CapacityRules;
    }

    public void UpsertCapacityRule(CapacityRule rule)
    {
        if (rule.AllowedMinutes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rule), "Allowed minutes must be zero or greater.");
        }

        var existing = _state.CapacityRules.FirstOrDefault(item => item.Id == rule.Id);
        if (existing is null)
        {
            _state.CapacityRules.Add(new CapacityRule
            {
                Id = rule.Id == Guid.Empty ? Guid.NewGuid() : rule.Id,
                GameId = rule.GameId,
                Period = rule.Period,
                AllowedMinutes = rule.AllowedMinutes,
                IsEnabled = rule.IsEnabled
            });
        }
        else
        {
            existing.GameId = rule.GameId;
            existing.Period = rule.Period;
            existing.AllowedMinutes = rule.AllowedMinutes;
            existing.IsEnabled = rule.IsEnabled;
        }

        SaveAndNotify();
    }

    public bool DeleteCapacityRule(Guid ruleId)
    {
        var existing = _state.CapacityRules.FirstOrDefault(item => item.Id == ruleId);
        if (existing is null)
        {
            return false;
        }

        _state.CapacityRules.Remove(existing);
        SaveAndNotify();
        return true;
    }

    public CapacitySnapshot GetCapacitySnapshot(Guid? gameId, CapacityPeriod period, int allowedMinutes)
    {
        var now = DateTimeOffset.Now;
        var start = GetPeriodStart(now, period);

        var used = _state.Intervals
            .Where(interval => gameId is null || interval.GameId == gameId.Value)
            .Select(interval => GetActiveDurationWithin(interval, now, start, now))
            .Aggregate(TimeSpan.Zero, (total, next) => total + next);

        var allowed = TimeSpan.FromMinutes(allowedMinutes);
        return new CapacitySnapshot(allowed, used, allowed - used);
    }

    public CapacityVerdict GetCapacityVerdict(CapacityRule rule)
    {
        var snapshot = GetCapacitySnapshot(rule.GameId, rule.Period, rule.AllowedMinutes);
        return new CapacityVerdict(rule.GameId, rule.Period, snapshot.Allowed, snapshot.Used, snapshot.Remaining);
    }

    private static DateTimeOffset GetPeriodStart(DateTimeOffset now, CapacityPeriod period)
    {
        return period switch
        {
            CapacityPeriod.Day => new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset),
            CapacityPeriod.Week => StartOfWeek(now),
            CapacityPeriod.Month => new DateTimeOffset(now.Year, now.Month, 1, 0, 0, 0, now.Offset),
            _ => now
        };
    }

    private static TimeSpan GetIntervalDurationWithin(
        PlayInterval interval,
        DateTimeOffset now,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        var intervalEnd = interval.EndedAt ?? now;
        var start = from is null || interval.StartedAt > from.Value ? interval.StartedAt : from.Value;
        var end = to is null || intervalEnd < to.Value ? intervalEnd : to.Value;

        return end > start ? end - start : TimeSpan.Zero;
    }

    private static TimeSpan GetActiveDurationWithin(
        PlayInterval interval,
        DateTimeOffset now,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        if (interval.Source != IntervalSource.ProcessTracker)
        {
            return GetIntervalDurationWithin(interval, now, from, to);
        }

        return interval.ActiveIntervals
            .Select(active => GetActiveIntervalDurationWithin(active, now, from, to))
            .Aggregate(TimeSpan.Zero, (total, next) => total + next);
    }

    private static TimeSpan GetActiveIntervalDurationWithin(
        ActivePlayInterval interval,
        DateTimeOffset now,
        DateTimeOffset? from,
        DateTimeOffset? to)
    {
        var intervalEnd = interval.EndedAt ?? now;
        var start = from is null || interval.StartedAt > from.Value ? interval.StartedAt : from.Value;
        var end = to is null || intervalEnd < to.Value ? intervalEnd : to.Value;

        return end > start ? end - start : TimeSpan.Zero;
    }

    private static ActivePlayInterval? GetOpenActiveInterval(PlayInterval interval)
    {
        return interval.ActiveIntervals.LastOrDefault(active => active.EndedAt is null);
    }

    private static DateTimeOffset StartOfWeek(DateTimeOffset now)
    {
        var diff = (7 + (now.DayOfWeek - DayOfWeek.Monday)) % 7;
        var startDate = now.Date.AddDays(-diff);
        return new DateTimeOffset(startDate, now.Offset);
    }

    private void SaveAndNotify()
    {
        _store.Save(_state);
        TrackingChanged?.Invoke(this, EventArgs.Empty);
    }

    private static string NormalizeProcessName(string processName)
    {
        var trimmed = processName.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".exe";
    }

    public void Dispose()
    {
        Stop();
        _timer.Dispose();
    }
}

public readonly record struct CapacitySnapshot(TimeSpan Allowed, TimeSpan Used, TimeSpan Remaining);
public readonly record struct CapacityVerdict(
    Guid? GameId,
    CapacityPeriod Period,
    TimeSpan Allowed,
    TimeSpan Used,
    TimeSpan Remaining)
{
    public bool IsOverCapacity => Remaining < TimeSpan.Zero;
}
