namespace GameTimeTracker;

public interface IGameStore
{
    TrackerState Load();
    void Save(TrackerState state);
}
