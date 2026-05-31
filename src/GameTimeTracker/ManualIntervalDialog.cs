using System.Windows.Forms;

namespace GameTimeTracker;

public sealed class ManualIntervalDialog : Form
{
    private readonly ComboBox _game = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly ComboBox _source = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short, Width = 140 };
    private readonly DateTimePicker _start = new() { Format = DateTimePickerFormat.Time, ShowUpDown = true, Width = 140 };
    private readonly NumericUpDown _minutes = new() { Minimum = 1, Maximum = 10080, Value = 30, Increment = 15, Width = 140 };
    private readonly TextBox _note = new() { Width = 280 };
    private readonly List<GameProfile> _games;
    private readonly PlayInterval? _existingInterval;

    public PlayInterval Interval { get; private set; } = new();

    public ManualIntervalDialog(List<GameProfile> games) : this(games, null)
    {
    }

    public ManualIntervalDialog(List<GameProfile> games, PlayInterval? interval)
    {
        _games = games;
        _existingInterval = interval;
        Text = interval is null ? "Add time" : "Edit time";
        Width = 420;
        Height = 320;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        foreach (var game in games.OrderBy(game => game.DisplayName))
        {
            _game.Items.Add(new GameItem(game));
        }

        if (_game.Items.Count > 0)
        {
            _game.SelectedIndex = 0;
        }

        foreach (var source in Enum.GetValues<IntervalSource>())
        {
            _source.Items.Add(source);
        }

        _source.SelectedItem = IntervalSource.ManualAdjustment;
        _date.Value = interval?.StartedAt.LocalDateTime.Date ?? DateTime.Now;
        _start.Value = interval?.StartedAt.LocalDateTime ?? DateTime.Now;

        if (interval is not null)
        {
            SelectGame(interval.GameId);
            _source.SelectedItem = interval.Source;
            _minutes.Value = Math.Clamp((decimal)interval.Duration(DateTimeOffset.Now).TotalMinutes, _minutes.Minimum, _minutes.Maximum);
            _note.Text = interval.Note ?? "";
        }

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => SaveInterval();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 7,
            AutoSize = true
        };

        layout.Controls.Add(new Label { Text = "Game", AutoSize = true }, 0, 0);
        layout.Controls.Add(_game, 1, 0);
        layout.Controls.Add(new Label { Text = "Source", AutoSize = true }, 0, 1);
        layout.Controls.Add(_source, 1, 1);
        layout.Controls.Add(new Label { Text = "Date", AutoSize = true }, 0, 2);
        layout.Controls.Add(_date, 1, 2);
        layout.Controls.Add(new Label { Text = "Start", AutoSize = true }, 0, 3);
        layout.Controls.Add(_start, 1, 3);
        layout.Controls.Add(new Label { Text = "Minutes", AutoSize = true }, 0, 4);
        layout.Controls.Add(_minutes, 1, 4);
        layout.Controls.Add(new Label { Text = "Note", AutoSize = true }, 0, 5);
        layout.Controls.Add(_note, 1, 5);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 1, 6);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(layout);
    }

    private void SaveInterval()
    {
        if (_game.SelectedItem is not GameItem selected)
        {
            DialogResult = DialogResult.None;
            return;
        }

        var start = new DateTimeOffset(
            _date.Value.Year,
            _date.Value.Month,
            _date.Value.Day,
            _start.Value.Hour,
            _start.Value.Minute,
            0,
            DateTimeOffset.Now.Offset);

        Interval = new PlayInterval
        {
            Id = _existingInterval?.Id ?? Guid.NewGuid(),
            GameId = selected.Game.Id,
            StartedAt = start,
            EndedAt = start.AddMinutes((int)_minutes.Value),
            Source = _source.SelectedItem is IntervalSource source ? source : IntervalSource.ManualAdjustment,
            Note = string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim()
        };
    }

    private void SelectGame(Guid gameId)
    {
        for (var i = 0; i < _game.Items.Count; i++)
        {
            if (_game.Items[i] is GameItem item && item.Game.Id == gameId)
            {
                _game.SelectedIndex = i;
                return;
            }
        }
    }

    private sealed class GameItem
    {
        public GameItem(GameProfile game) => Game = game;
        public GameProfile Game { get; }
        public override string ToString() => Game.DisplayName;
    }
}

