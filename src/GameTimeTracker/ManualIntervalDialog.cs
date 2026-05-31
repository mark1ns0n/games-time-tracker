using System.Windows.Forms;

namespace GameTimeTracker;

public sealed class ManualIntervalDialog : Form
{
    private readonly ComboBox _game = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 280 };
    private readonly DateTimePicker _date = new() { Format = DateTimePickerFormat.Short, Width = 140 };
    private readonly DateTimePicker _start = new() { Format = DateTimePickerFormat.Time, ShowUpDown = true, Width = 140 };
    private readonly NumericUpDown _minutes = new() { Minimum = 1, Maximum = 1440, Value = 30, Increment = 15, Width = 140 };
    private readonly TextBox _note = new() { Width = 280 };
    private readonly List<GameProfile> _games;

    public PlayInterval Interval { get; private set; } = new();

    public ManualIntervalDialog(List<GameProfile> games)
    {
        _games = games;
        Text = "Add time";
        Width = 420;
        Height = 280;
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

        _date.Value = DateTime.Now;
        _start.Value = DateTime.Now;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => SaveInterval();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 6,
            AutoSize = true
        };

        layout.Controls.Add(new Label { Text = "Game", AutoSize = true }, 0, 0);
        layout.Controls.Add(_game, 1, 0);
        layout.Controls.Add(new Label { Text = "Date", AutoSize = true }, 0, 1);
        layout.Controls.Add(_date, 1, 1);
        layout.Controls.Add(new Label { Text = "Start", AutoSize = true }, 0, 2);
        layout.Controls.Add(_start, 1, 2);
        layout.Controls.Add(new Label { Text = "Minutes", AutoSize = true }, 0, 3);
        layout.Controls.Add(_minutes, 1, 3);
        layout.Controls.Add(new Label { Text = "Note", AutoSize = true }, 0, 4);
        layout.Controls.Add(_note, 1, 4);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 1, 5);

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
            GameId = selected.Game.Id,
            StartedAt = start,
            EndedAt = start.AddMinutes((int)_minutes.Value),
            Source = IntervalSource.ManualAdjustment,
            Note = string.IsNullOrWhiteSpace(_note.Text) ? null : _note.Text.Trim()
        };
    }

    private sealed class GameItem
    {
        public GameItem(GameProfile game) => Game = game;
        public GameProfile Game { get; }
        public override string ToString() => Game.DisplayName;
    }
}

