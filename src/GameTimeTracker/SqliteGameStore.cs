using System.Globalization;

using Microsoft.Data.Sqlite;

namespace GameTimeTracker;

public sealed class SqliteGameStore : IGameStore
{
    private const string SchemaVersion = "1";
    private const string MigrationMetadataKey = "json_migration_complete";
    private readonly string _databasePath;
    private readonly string? _jsonMigrationPath;

    public SqliteGameStore(string databasePath, string? jsonMigrationPath = null)
    {
        _databasePath = databasePath;
        _jsonMigrationPath = jsonMigrationPath;
    }

    public TrackerState Load()
    {
        using var connection = OpenConnection();
        InitializeSchema(connection);

        if (!IsJsonMigrationComplete(connection))
        {
            var migrated = TryMigrateJson(connection);
            if (migrated is not null)
            {
                return migrated;
            }
        }

        var state = new TrackerState();
        LoadGames(connection, state);
        LoadIntervals(connection, state);
        LoadCapacityRules(connection, state);
        return state;
    }

    public void Save(TrackerState state)
    {
        using var connection = OpenConnection();
        InitializeSchema(connection);

        using var transaction = connection.BeginTransaction();
        Execute(connection, transaction, "DELETE FROM active_play_intervals;");
        Execute(connection, transaction, "DELETE FROM play_intervals;");
        Execute(connection, transaction, "DELETE FROM game_processes;");
        Execute(connection, transaction, "DELETE FROM capacity_rules;");
        Execute(connection, transaction, "DELETE FROM game_profiles;");

        SaveGames(connection, transaction, state.Games);
        SaveIntervals(connection, transaction, state.Intervals);
        SaveCapacityRules(connection, transaction, state.CapacityRules);
        SetMetadata(connection, transaction, "schema_version", SchemaVersion);
        SetMetadata(connection, transaction, MigrationMetadataKey, "true");
        transaction.Commit();
    }

    private SqliteConnection OpenConnection()
    {
        var directory = Path.GetDirectoryName(_databasePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate
        };

        var connection = new SqliteConnection(builder.ToString());
        connection.Open();
        Execute(connection, null, "PRAGMA foreign_keys = ON;");
        return connection;
    }

    private static void InitializeSchema(SqliteConnection connection)
    {
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS metadata (
                key TEXT PRIMARY KEY,
                value TEXT NOT NULL
            );
            """);
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS game_profiles (
                id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                executable_path TEXT NULL,
                icon_path TEXT NULL,
                is_enabled INTEGER NOT NULL
            );
            """);
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS game_processes (
                game_id TEXT NOT NULL,
                process_name TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                PRIMARY KEY (game_id, process_name),
                FOREIGN KEY (game_id) REFERENCES game_profiles(id) ON DELETE CASCADE
            );
            """);
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS play_intervals (
                id TEXT PRIMARY KEY,
                game_id TEXT NOT NULL,
                started_at TEXT NOT NULL,
                ended_at TEXT NULL,
                source INTEGER NOT NULL,
                note TEXT NULL
            );
            """);
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS active_play_intervals (
                interval_id TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                ended_at TEXT NULL,
                PRIMARY KEY (interval_id, sort_order),
                FOREIGN KEY (interval_id) REFERENCES play_intervals(id) ON DELETE CASCADE
            );
            """);
        Execute(connection, null, """
            CREATE TABLE IF NOT EXISTS capacity_rules (
                id TEXT PRIMARY KEY,
                game_id TEXT NULL,
                period INTEGER NOT NULL,
                allowed_minutes INTEGER NOT NULL,
                is_enabled INTEGER NOT NULL
            );
            """);
        Execute(connection, null, "CREATE INDEX IF NOT EXISTS idx_game_processes_process_name ON game_processes(process_name);");
        Execute(connection, null, "CREATE INDEX IF NOT EXISTS idx_play_intervals_game_started ON play_intervals(game_id, started_at);");
        Execute(connection, null, "CREATE INDEX IF NOT EXISTS idx_play_intervals_started ON play_intervals(started_at);");
        Execute(connection, null, "CREATE INDEX IF NOT EXISTS idx_active_intervals_interval_started ON active_play_intervals(interval_id, started_at);");
        Execute(connection, null, "CREATE INDEX IF NOT EXISTS idx_capacity_rules_game_period ON capacity_rules(game_id, period);");
        SetMetadata(connection, null, "schema_version", SchemaVersion);
    }

    private TrackerState? TryMigrateJson(SqliteConnection connection)
    {
        if (!IsDomainEmpty(connection) || string.IsNullOrWhiteSpace(_jsonMigrationPath) || !File.Exists(_jsonMigrationPath))
        {
            SetMetadata(connection, null, MigrationMetadataKey, "true");
            return null;
        }

        var jsonStore = new JsonGameStore(_jsonMigrationPath);
        var state = jsonStore.Load();
        Save(state);
        return state;
    }

    private static bool IsJsonMigrationComplete(SqliteConnection connection)
    {
        return string.Equals(GetMetadata(connection, MigrationMetadataKey), "true", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsDomainEmpty(SqliteConnection connection)
    {
        return GetCount(connection, "game_profiles") == 0
            && GetCount(connection, "play_intervals") == 0
            && GetCount(connection, "capacity_rules") == 0;
    }

    private static void LoadGames(SqliteConnection connection, TrackerState state)
    {
        var processesByGame = new Dictionary<Guid, List<string>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT game_id, process_name
                FROM game_processes
                ORDER BY game_id, sort_order, process_name;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var gameId = Guid.Parse(reader.GetString(0));
                if (!processesByGame.TryGetValue(gameId, out var processes))
                {
                    processes = [];
                    processesByGame[gameId] = processes;
                }

                processes.Add(reader.GetString(1));
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, display_name, executable_path, icon_path, is_enabled
                FROM game_profiles
                ORDER BY display_name;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = Guid.Parse(reader.GetString(0));
                state.Games.Add(new GameProfile
                {
                    Id = id,
                    DisplayName = reader.GetString(1),
                    ExecutablePath = GetNullableString(reader, 2),
                    IconPath = GetNullableString(reader, 3),
                    IsEnabled = reader.GetInt32(4) != 0,
                    ProcessNames = processesByGame.TryGetValue(id, out var processes) ? processes : []
                });
            }
        }
    }

    private static void LoadIntervals(SqliteConnection connection, TrackerState state)
    {
        var activeByInterval = new Dictionary<Guid, List<ActivePlayInterval>>();
        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT interval_id, started_at, ended_at
                FROM active_play_intervals
                ORDER BY interval_id, sort_order;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var intervalId = Guid.Parse(reader.GetString(0));
                if (!activeByInterval.TryGetValue(intervalId, out var activeIntervals))
                {
                    activeIntervals = [];
                    activeByInterval[intervalId] = activeIntervals;
                }

                activeIntervals.Add(new ActivePlayInterval
                {
                    StartedAt = ParseDateTimeOffset(reader.GetString(1)),
                    EndedAt = ParseNullableDateTimeOffset(reader, 2)
                });
            }
        }

        using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                SELECT id, game_id, started_at, ended_at, source, note
                FROM play_intervals
                ORDER BY started_at;
                """;
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var id = Guid.Parse(reader.GetString(0));
                state.Intervals.Add(new PlayInterval
                {
                    Id = id,
                    GameId = Guid.Parse(reader.GetString(1)),
                    StartedAt = ParseDateTimeOffset(reader.GetString(2)),
                    EndedAt = ParseNullableDateTimeOffset(reader, 3),
                    Source = (IntervalSource)reader.GetInt32(4),
                    Note = GetNullableString(reader, 5),
                    ActiveIntervals = activeByInterval.TryGetValue(id, out var activeIntervals) ? activeIntervals : []
                });
            }
        }
    }

    private static void LoadCapacityRules(SqliteConnection connection, TrackerState state)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, game_id, period, allowed_minutes, is_enabled
            FROM capacity_rules
            ORDER BY game_id IS NOT NULL, game_id, period;
            """;
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            state.CapacityRules.Add(new CapacityRule
            {
                Id = Guid.Parse(reader.GetString(0)),
                GameId = ParseNullableGuid(reader, 1),
                Period = (CapacityPeriod)reader.GetInt32(2),
                AllowedMinutes = reader.GetInt32(3),
                IsEnabled = reader.GetInt32(4) != 0
            });
        }
    }

    private static void SaveGames(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<GameProfile> games)
    {
        foreach (var game in games)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO game_profiles (id, display_name, executable_path, icon_path, is_enabled)
                    VALUES ($id, $display_name, $executable_path, $icon_path, $is_enabled);
                    """;
                command.Parameters.AddWithValue("$id", game.Id.ToString());
                command.Parameters.AddWithValue("$display_name", game.DisplayName);
                AddNullableString(command, "$executable_path", game.ExecutablePath);
                AddNullableString(command, "$icon_path", game.IconPath);
                command.Parameters.AddWithValue("$is_enabled", game.IsEnabled ? 1 : 0);
                command.ExecuteNonQuery();
            }

            var processNames = game.ProcessNames
                .Where(processName => !string.IsNullOrWhiteSpace(processName))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            for (var i = 0; i < processNames.Count; i++)
            {
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO game_processes (game_id, process_name, sort_order)
                    VALUES ($game_id, $process_name, $sort_order);
                    """;
                command.Parameters.AddWithValue("$game_id", game.Id.ToString());
                command.Parameters.AddWithValue("$process_name", processNames[i]);
                command.Parameters.AddWithValue("$sort_order", i);
                command.ExecuteNonQuery();
            }
        }
    }

    private static void SaveIntervals(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<PlayInterval> intervals)
    {
        foreach (var interval in intervals)
        {
            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO play_intervals (id, game_id, started_at, ended_at, source, note)
                    VALUES ($id, $game_id, $started_at, $ended_at, $source, $note);
                    """;
                command.Parameters.AddWithValue("$id", interval.Id.ToString());
                command.Parameters.AddWithValue("$game_id", interval.GameId.ToString());
                command.Parameters.AddWithValue("$started_at", FormatDateTimeOffset(interval.StartedAt));
                AddNullableDateTimeOffset(command, "$ended_at", interval.EndedAt);
                command.Parameters.AddWithValue("$source", (int)interval.Source);
                AddNullableString(command, "$note", interval.Note);
                command.ExecuteNonQuery();
            }

            for (var i = 0; i < interval.ActiveIntervals.Count; i++)
            {
                var active = interval.ActiveIntervals[i];
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO active_play_intervals (interval_id, sort_order, started_at, ended_at)
                    VALUES ($interval_id, $sort_order, $started_at, $ended_at);
                    """;
                command.Parameters.AddWithValue("$interval_id", interval.Id.ToString());
                command.Parameters.AddWithValue("$sort_order", i);
                command.Parameters.AddWithValue("$started_at", FormatDateTimeOffset(active.StartedAt));
                AddNullableDateTimeOffset(command, "$ended_at", active.EndedAt);
                command.ExecuteNonQuery();
            }
        }
    }

    private static void SaveCapacityRules(SqliteConnection connection, SqliteTransaction transaction, IEnumerable<CapacityRule> rules)
    {
        foreach (var rule in rules)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO capacity_rules (id, game_id, period, allowed_minutes, is_enabled)
                VALUES ($id, $game_id, $period, $allowed_minutes, $is_enabled);
                """;
            command.Parameters.AddWithValue("$id", rule.Id.ToString());
            AddNullableGuid(command, "$game_id", rule.GameId);
            command.Parameters.AddWithValue("$period", (int)rule.Period);
            command.Parameters.AddWithValue("$allowed_minutes", rule.AllowedMinutes);
            command.Parameters.AddWithValue("$is_enabled", rule.IsEnabled ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    private static void SetMetadata(SqliteConnection connection, SqliteTransaction? transaction, string key, string value)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO metadata (key, value)
            VALUES ($key, $value)
            ON CONFLICT(key) DO UPDATE SET value = excluded.value;
            """;
        command.Parameters.AddWithValue("$key", key);
        command.Parameters.AddWithValue("$value", value);
        command.ExecuteNonQuery();
    }

    private static string? GetMetadata(SqliteConnection connection, string key)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT value FROM metadata WHERE key = $key;";
        command.Parameters.AddWithValue("$key", key);
        return command.ExecuteScalar() as string;
    }

    private static long GetCount(SqliteConnection connection, string table)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table};";
        return (long)(command.ExecuteScalar() ?? 0L);
    }

    private static void Execute(SqliteConnection connection, SqliteTransaction? transaction, string commandText)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }

    private static void AddNullableString(SqliteCommand command, string name, string? value)
    {
        command.Parameters.AddWithValue(name, string.IsNullOrWhiteSpace(value) ? DBNull.Value : value);
    }

    private static string? GetNullableString(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);
    }

    private static void AddNullableGuid(SqliteCommand command, string name, Guid? value)
    {
        command.Parameters.AddWithValue(name, value is null ? DBNull.Value : value.Value.ToString());
    }

    private static Guid? ParseNullableGuid(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : Guid.Parse(reader.GetString(ordinal));
    }

    private static void AddNullableDateTimeOffset(SqliteCommand command, string name, DateTimeOffset? value)
    {
        command.Parameters.AddWithValue(name, value is null ? DBNull.Value : FormatDateTimeOffset(value.Value));
    }

    private static DateTimeOffset? ParseNullableDateTimeOffset(SqliteDataReader reader, int ordinal)
    {
        return reader.IsDBNull(ordinal) ? null : ParseDateTimeOffset(reader.GetString(ordinal));
    }

    private static string FormatDateTimeOffset(DateTimeOffset value)
    {
        return value.ToString("O", CultureInfo.InvariantCulture);
    }

    private static DateTimeOffset ParseDateTimeOffset(string value)
    {
        return DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
    }
}
