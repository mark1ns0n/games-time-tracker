# Architecture Notes

## Storage v1

MVP uses `%LOCALAPPDATA%\GameTimeTracker\tracker-data.json` so the first build has no database dependency. The model is already interval-based, so moving to SQLite later should be a storage implementation swap, not a product rewrite.

## Tracking v1

The tracker polls Windows processes every 5 seconds. A mapped game opens a play interval when any configured process is running and closes it when all configured processes disappear.

## Capacity v1

Capacity is calculated from intervals, including manual adjustments. A daily budget can answer: "do I still have time to play today?"

## Next storage step

When the MVP feels right, migrate storage to SQLite with tables for `game_profiles`, `game_processes`, `play_intervals`, `capacity_rules`, and `settings`.
