using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace GameTimeTracker;

public sealed class MainForm : Form
{
    private static readonly Size GameIconSize = new(32, 32);

    private readonly TrackerState _state;
    private readonly TrackingEngine _tracker;
    private readonly JsonGameStore _store;
    private readonly BindingList<GameRow> _rows = [];
    private readonly BindingList<IntervalRow> _intervalRows = [];
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = false, ReadOnly = true };
    private readonly DataGridView _intervalGrid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true, ReadOnly = true };
    private readonly Dictionary<string, Image> _iconCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Image _fallbackIcon = IconImageLoader.CreateFallback(GameIconSize);
    private readonly Button _addGameButton = new() { Text = "Add game", AutoSize = true };
    private readonly Button _importSteamButton = new() { Text = "Import Steam", AutoSize = true };
    private readonly Button _importEpicButton = new() { Text = "Import Epic", AutoSize = true };
    private readonly Button _addIntervalButton = new() { Text = "Add time", AutoSize = true };
    private readonly Button _editIntervalButton = new() { Text = "Edit time", AutoSize = true };
    private readonly Button _deleteIntervalButton = new() { Text = "Delete time", AutoSize = true };
    private readonly Button _capacityRulesButton = new() { Text = "Capacity rules", AutoSize = true };
    private readonly Button _refreshButton = new() { Text = "Refresh", AutoSize = true };
    private readonly Label _capacityLabel = new() { AutoSize = true, Padding = new Padding(8, 6, 0, 0) };

    public MainForm(TrackerState state, TrackingEngine tracker, JsonGameStore store)
    {
        _state = state;
        _tracker = tracker;
        _store = store;

        Text = "GameTimeTracker";
        Width = 1180;
        Height = 560;
        MinimumSize = new Size(760, 420);

        ConfigureGameGrid();
        _grid.DataSource = _rows;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.SelectionChanged += (_, _) => RefreshIntervalRows();
        _grid.CellFormatting += FormatGameGridIcon;

        _intervalGrid.DataSource = _intervalRows;
        _intervalGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _intervalGrid.MultiSelect = false;
        _intervalGrid.DataBindingComplete += (_, _) =>
        {
            if (_intervalGrid.Columns[nameof(IntervalRow.IntervalId)] is { } idColumn)
            {
                idColumn.Visible = false;
            }
        };
        _addGameButton.Click += (_, _) => AddGame();
        _importSteamButton.Click += (_, _) => ImportSteamGames();
        _importEpicButton.Click += (_, _) => ImportEpicGames();
        _addIntervalButton.Click += (_, _) => AddManualInterval();
        _editIntervalButton.Click += (_, _) => EditSelectedInterval();
        _deleteIntervalButton.Click += (_, _) => DeleteSelectedInterval();
        _capacityRulesButton.Click += (_, _) => EditCapacityRules();
        _refreshButton.Click += (_, _) => RefreshRows();
        _tracker.TrackingChanged += (_, _) => RefreshRows();

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            WrapContents = false
        };

        toolbar.Controls.Add(_addGameButton);
        toolbar.Controls.Add(_importSteamButton);
        toolbar.Controls.Add(_importEpicButton);
        toolbar.Controls.Add(_addIntervalButton);
        toolbar.Controls.Add(_editIntervalButton);
        toolbar.Controls.Add(_deleteIntervalButton);
        toolbar.Controls.Add(_capacityRulesButton);
        toolbar.Controls.Add(_refreshButton);
        toolbar.Controls.Add(_capacityLabel);

        var split = new SplitContainer
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Horizontal,
            SplitterDistance = 260
        };
        split.Panel1.Controls.Add(_grid);
        split.Panel2.Controls.Add(_intervalGrid);

        Controls.Add(split);
        Controls.Add(toolbar);

        Load += (_, _) =>
        {
            _tracker.Start();
            RefreshRows();
        };

        FormClosing += (_, _) =>
        {
            _tracker.Dispose();
            _store.Save(_state);
            DisposeIcons();
        };
    }

    private void ConfigureGameGrid()
    {
        _grid.RowTemplate.Height = 38;
        _grid.Columns.Add(new DataGridViewImageColumn
        {
            Name = "Icon",
            HeaderText = "",
            Width = 42,
            ImageLayout = DataGridViewImageCellLayout.Zoom,
            Resizable = DataGridViewTriState.False,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = nameof(GameRow.Name), DataPropertyName = nameof(GameRow.Name), HeaderText = "Name", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = nameof(GameRow.Processes), DataPropertyName = nameof(GameRow.Processes), HeaderText = "Processes", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, FillWeight = 170 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = nameof(GameRow.Status), DataPropertyName = nameof(GameRow.Status), HeaderText = "Status", Width = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = nameof(GameRow.TodayRunning), DataPropertyName = nameof(GameRow.TodayRunning), HeaderText = "Today running", Width = 105 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = nameof(GameRow.TodayActive), DataPropertyName = nameof(GameRow.TodayActive), HeaderText = "Today active", Width = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = nameof(GameRow.TotalRunning), DataPropertyName = nameof(GameRow.TotalRunning), HeaderText = "Total running", Width = 105 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = nameof(GameRow.TotalActive), DataPropertyName = nameof(GameRow.TotalActive), HeaderText = "Total active", Width = 100 });
    }

    private void FormatGameGridIcon(object? sender, DataGridViewCellFormattingEventArgs e)
    {
        if (_grid.Columns[e.ColumnIndex].Name != "Icon")
        {
            return;
        }

        if (_grid.Rows[e.RowIndex].DataBoundItem is GameRow row)
        {
            e.Value = GetGameIcon(row.IconPath);
            e.FormattingApplied = true;
        }
    }

    private Image GetGameIcon(string? iconPath)
    {
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            return _fallbackIcon;
        }

        var key = Environment.ExpandEnvironmentVariables(iconPath.Trim());
        if (_iconCache.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var icon = IconImageLoader.LoadBitmap(key, GameIconSize);
        if (icon is null)
        {
            return _fallbackIcon;
        }

        _iconCache[key] = icon;
        return icon;
    }

    private void DisposeIcons()
    {
        foreach (var icon in _iconCache.Values)
        {
            icon.Dispose();
        }

        _iconCache.Clear();
        _fallbackIcon.Dispose();
    }

    private void AddGame()
    {
        using var dialog = new GameProfileDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _state.Games.Add(dialog.Game);
        _store.Save(_state);
        _tracker.Poll();
        RefreshRows();
    }

    private void ImportSteamGames()
    {
        var service = new SteamLibraryImportService();
        var candidates = service.FindCandidates(_state.Games);
        if (candidates.Count == 0)
        {
            MessageBox.Show(this, "No new installed Steam games were found.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new SteamImportDialog(candidates);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        foreach (var game in dialog.SelectedGames)
        {
            _state.Games.Add(game);
        }

        _store.Save(_state);
        _tracker.Poll();
        RefreshRows();
    }

    private void ImportEpicGames()
    {
        var service = new EpicLibraryImportService();
        var candidates = service.FindCandidates(_state.Games);
        if (candidates.Count == 0)
        {
            MessageBox.Show(this, "No new installed Epic games were found.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new EpicImportDialog(candidates);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        foreach (var game in dialog.SelectedGames)
        {
            _state.Games.Add(game);
        }

        _store.Save(_state);
        _tracker.Poll();
        RefreshRows();
    }

    private void AddManualInterval()
    {
        if (_state.Games.Count == 0)
        {
            MessageBox.Show(this, "Add a game first.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new ManualIntervalDialog(_state.Games);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _state.Intervals.Add(dialog.Interval);
        _store.Save(_state);
        RefreshRows();
    }

    private void EditSelectedInterval()
    {
        var interval = GetSelectedInterval();
        if (interval is null)
        {
            MessageBox.Show(this, "Select an interval first.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (interval.EndedAt is null)
        {
            MessageBox.Show(this, "Running intervals can be edited after the game stops.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        using var dialog = new ManualIntervalDialog(_state.Games, interval);
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        interval.GameId = dialog.Interval.GameId;
        interval.StartedAt = dialog.Interval.StartedAt;
        interval.EndedAt = dialog.Interval.EndedAt;
        interval.Source = dialog.Interval.Source;
        interval.Note = dialog.Interval.Note;

        _store.Save(_state);
        RefreshRows();
    }

    private void DeleteSelectedInterval()
    {
        var interval = GetSelectedInterval();
        if (interval is null)
        {
            MessageBox.Show(this, "Select an interval first.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (interval.EndedAt is null)
        {
            MessageBox.Show(this, "Running intervals can be deleted after the game stops.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(
            this,
            $"Delete interval from {FormatDateTime(interval.StartedAt)}?",
            "GameTimeTracker",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        _state.Intervals.Remove(interval);
        _store.Save(_state);
        RefreshRows();
    }

    private void EditCapacityRules()
    {
        using var dialog = new CapacityRulesDialog(_state.Games, _tracker);
        dialog.ShowDialog(this);
        RefreshRows();
    }

    private void RefreshRows()
    {
        var selectedGameId = GetSelectedGameId();
        var now = DateTimeOffset.Now;
        _rows.Clear();

        foreach (var game in _state.Games.OrderBy(game => game.DisplayName))
        {
            var open = _tracker.GetOpenInterval(game.Id);
            var todayStart = new DateTimeOffset(now.Year, now.Month, now.Day, 0, 0, 0, now.Offset);
            var totalRunning = _tracker.GetTotalPlayed(game.Id);
            var totalActive = _tracker.GetTotalActive(game.Id);
            var todayRunning = _tracker.GetTotalPlayed(game.Id, todayStart);
            var todayActive = _tracker.GetTotalActive(game.Id, todayStart);
            var status = open is null ? "No" : _tracker.IsGameActive(game.Id) ? "Active" : "Background";

            _rows.Add(new GameRow(
                game.Id,
                game.DisplayName,
                string.Join(", ", game.ProcessNames),
                status,
                FormatHours(todayRunning),
                FormatHours(todayActive),
                FormatHours(totalRunning),
                FormatHours(totalActive),
                game.IconPath ?? ""));
        }

        _capacityLabel.Text = FormatCapacityStatus();

        if (selectedGameId is not null)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].GameId == selectedGameId.Value)
                {
                    _grid.ClearSelection();
                    _grid.Rows[i].Selected = true;
                    _grid.CurrentCell = _grid.Rows[i].Cells[nameof(GameRow.Name)];
                    break;
                }
            }
        }

        RefreshIntervalRows();
    }

    private void RefreshIntervalRows()
    {
        var now = DateTimeOffset.Now;
        var selectedGameId = GetSelectedGameId();
        _intervalRows.Clear();

        if (selectedGameId is null)
        {
            return;
        }

        foreach (var interval in _state.Intervals
            .Where(interval => interval.GameId == selectedGameId.Value)
            .OrderByDescending(interval => interval.StartedAt))
        {
            _intervalRows.Add(new IntervalRow(
                interval.Id,
                FormatDateTime(interval.StartedAt),
                interval.EndedAt is null ? "Running" : FormatDateTime(interval.EndedAt.Value),
                FormatHours(interval.Duration(now)),
                FormatHours(_tracker.GetActiveDuration(interval, now)),
                interval.Source.ToString(),
                interval.Note ?? ""));
        }
    }

    private Guid? GetSelectedGameId()
    {
        if (_grid.CurrentRow?.DataBoundItem is GameRow selected)
        {
            return selected.GameId;
        }

        return _rows.Count == 0 ? null : _rows[0].GameId;
    }

    private PlayInterval? GetSelectedInterval()
    {
        if (_intervalGrid.CurrentRow?.DataBoundItem is not IntervalRow selected)
        {
            return null;
        }

        return _state.Intervals.FirstOrDefault(interval => interval.Id == selected.IntervalId);
    }

    private string FormatCapacityStatus()
    {
        var selectedGameId = GetSelectedGameId();
        var rule = GetCapacityRuleFor(selectedGameId);

        if (rule is null)
        {
            return "No capacity rule set";
        }

        var verdict = _tracker.GetCapacityVerdict(rule);
        var scope = GetCapacityScopeName(verdict.GameId);
        var status = verdict.IsOverCapacity
            ? $"over by {FormatHours(verdict.Remaining.Duration())}"
            : $"remaining {FormatHours(verdict.Remaining)}";

        return $"{scope} {verdict.Period} capacity: allowed {FormatHours(verdict.Allowed)} / used {FormatHours(verdict.Used)} / {status}";
    }

    private CapacityRule? GetCapacityRuleFor(Guid? selectedGameId)
    {
        var enabledRules = _state.CapacityRules
            .Where(rule => rule.IsEnabled)
            .ToList();

        if (selectedGameId is not null)
        {
            var gameRule = enabledRules
                .Where(rule => rule.GameId == selectedGameId.Value)
                .OrderBy(rule => rule.Period)
                .FirstOrDefault();

            if (gameRule is not null)
            {
                return gameRule;
            }
        }

        return enabledRules
            .Where(rule => rule.GameId is null)
            .OrderBy(rule => rule.Period)
            .FirstOrDefault()
            ?? enabledRules.OrderBy(rule => rule.Period).FirstOrDefault();
    }

    private string GetCapacityScopeName(Guid? gameId)
    {
        if (gameId is null)
        {
            return "All games";
        }

        return _state.Games.FirstOrDefault(game => game.Id == gameId.Value)?.DisplayName ?? "Selected game";
    }

    private static string FormatHours(TimeSpan value)
    {
        var sign = value < TimeSpan.Zero ? "-" : "";
        var duration = value.Duration();
        return $"{sign}{Math.Floor(duration.TotalHours):0}h {duration.Minutes:00}m";
    }

    private static string FormatDateTime(DateTimeOffset value)
    {
        return value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    }
}

public sealed record GameRow(
    Guid GameId,
    string Name,
    string Processes,
    string Status,
    string TodayRunning,
    string TodayActive,
    string TotalRunning,
    string TotalActive,
    string IconPath);
public sealed record IntervalRow(Guid IntervalId, string Start, string End, string Running, string Active, string Source, string Note);

