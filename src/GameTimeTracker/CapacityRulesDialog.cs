using System.ComponentModel;
using System.Windows.Forms;

namespace GameTimeTracker;

public sealed class CapacityRulesDialog : Form
{
    private readonly List<GameProfile> _games;
    private readonly TrackingEngine _tracker;
    private readonly BindingList<CapacityRuleRow> _rows = [];
    private readonly DataGridView _grid = new() { Dock = DockStyle.Fill, AutoGenerateColumns = true, ReadOnly = true };
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
    private readonly ComboBox _period = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 120 };
    private readonly NumericUpDown _minutes = new() { Minimum = 0, Maximum = 100000, Value = 120, Increment = 15, Width = 120 };
    private readonly CheckBox _enabled = new() { Text = "Enabled", Checked = true, AutoSize = true };
    private Guid? _selectedRuleId;

    public CapacityRulesDialog(List<GameProfile> games, TrackingEngine tracker)
    {
        _games = games;
        _tracker = tracker;

        Text = "Capacity rules";
        Width = 760;
        Height = 440;
        MinimumSize = new Size(640, 360);
        StartPosition = FormStartPosition.CenterParent;

        _grid.DataSource = _rows;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.MultiSelect = false;
        _grid.SelectionChanged += (_, _) => LoadSelectedRule();
        _grid.DataBindingComplete += (_, _) =>
        {
            if (_grid.Columns[nameof(CapacityRuleRow.RuleId)] is { } idColumn)
            {
                idColumn.Visible = false;
            }
        };

        _scope.Items.Add(new ScopeItem(null, "All games"));
        foreach (var game in _games.OrderBy(game => game.DisplayName))
        {
            _scope.Items.Add(new ScopeItem(game.Id, game.DisplayName));
        }

        foreach (var period in Enum.GetValues<CapacityPeriod>())
        {
            _period.Items.Add(period);
        }

        var newButton = new Button { Text = "New", AutoSize = true };
        var saveButton = new Button { Text = "Save", AutoSize = true };
        var deleteButton = new Button { Text = "Delete", AutoSize = true };
        var closeButton = new Button { Text = "Close", DialogResult = DialogResult.OK, AutoSize = true };

        newButton.Click += (_, _) => NewRule();
        saveButton.Click += (_, _) => SaveRule();
        deleteButton.Click += (_, _) => DeleteRule();

        var editor = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            ColumnCount = 8
        };
        editor.Controls.Add(new Label { Text = "Scope", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, 0, 0);
        editor.Controls.Add(_scope, 1, 0);
        editor.Controls.Add(new Label { Text = "Period", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, 2, 0);
        editor.Controls.Add(_period, 3, 0);
        editor.Controls.Add(new Label { Text = "Minutes", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, 4, 0);
        editor.Controls.Add(_minutes, 5, 0);
        editor.Controls.Add(_enabled, 6, 0);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            Padding = new Padding(8)
        };
        buttons.Controls.Add(closeButton);
        buttons.Controls.Add(deleteButton);
        buttons.Controls.Add(saveButton);
        buttons.Controls.Add(newButton);

        Controls.Add(_grid);
        Controls.Add(editor);
        Controls.Add(buttons);

        AcceptButton = saveButton;
        CancelButton = closeButton;

        NewRule();
        RefreshRows(null);
    }

    private void NewRule()
    {
        _selectedRuleId = null;
        if (_scope.Items.Count > 0)
        {
            _scope.SelectedIndex = 0;
        }

        _period.SelectedItem = CapacityPeriod.Day;
        _minutes.Value = 120;
        _enabled.Checked = true;
        _grid.ClearSelection();
    }

    private void SaveRule()
    {
        if (_scope.SelectedItem is not ScopeItem scope || _period.SelectedItem is not CapacityPeriod period)
        {
            DialogResult = DialogResult.None;
            return;
        }

        var rule = new CapacityRule
        {
            Id = _selectedRuleId ?? Guid.NewGuid(),
            GameId = scope.GameId,
            Period = period,
            AllowedMinutes = (int)_minutes.Value,
            IsEnabled = _enabled.Checked
        };

        _tracker.UpsertCapacityRule(rule);
        RefreshRows(rule.Id);
    }

    private void DeleteRule()
    {
        if (_selectedRuleId is null)
        {
            MessageBox.Show(this, "Select a rule first.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var answer = MessageBox.Show(
            this,
            "Delete selected capacity rule?",
            "GameTimeTracker",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning);

        if (answer != DialogResult.Yes)
        {
            return;
        }

        _tracker.DeleteCapacityRule(_selectedRuleId.Value);
        NewRule();
        RefreshRows(null);
    }

    private void LoadSelectedRule()
    {
        if (_grid.CurrentRow?.DataBoundItem is not CapacityRuleRow selected)
        {
            return;
        }

        var rule = _tracker.GetCapacityRules().FirstOrDefault(rule => rule.Id == selected.RuleId);
        if (rule is null)
        {
            return;
        }

        _selectedRuleId = rule.Id;
        SelectScope(rule.GameId);
        _period.SelectedItem = rule.Period;
        _minutes.Value = Math.Clamp(rule.AllowedMinutes, (int)_minutes.Minimum, (int)_minutes.Maximum);
        _enabled.Checked = rule.IsEnabled;
    }

    private void RefreshRows(Guid? selectedRuleId)
    {
        _rows.Clear();
        foreach (var rule in _tracker.GetCapacityRules()
            .OrderBy(rule => rule.GameId is null ? 0 : 1)
            .ThenBy(rule => GetScopeName(rule.GameId))
            .ThenBy(rule => rule.Period))
        {
            _rows.Add(new CapacityRuleRow(
                rule.Id,
                GetScopeName(rule.GameId),
                rule.Period.ToString(),
                rule.AllowedMinutes,
                rule.IsEnabled));
        }

        if (selectedRuleId is not null)
        {
            for (var i = 0; i < _rows.Count; i++)
            {
                if (_rows[i].RuleId == selectedRuleId.Value)
                {
                    _grid.ClearSelection();
                    _grid.Rows[i].Selected = true;
                    _grid.CurrentCell = _grid.Rows[i].Cells[nameof(CapacityRuleRow.Scope)];
                    break;
                }
            }
        }
    }

    private void SelectScope(Guid? gameId)
    {
        for (var i = 0; i < _scope.Items.Count; i++)
        {
            if (_scope.Items[i] is ScopeItem item && item.GameId == gameId)
            {
                _scope.SelectedIndex = i;
                return;
            }
        }

        if (_scope.Items.Count > 0)
        {
            _scope.SelectedIndex = 0;
        }
    }

    private string GetScopeName(Guid? gameId)
    {
        if (gameId is null)
        {
            return "All games";
        }

        return _games.FirstOrDefault(game => game.Id == gameId.Value)?.DisplayName ?? "Missing game";
    }

    private sealed class ScopeItem
    {
        public ScopeItem(Guid? gameId, string name)
        {
            GameId = gameId;
            Name = name;
        }

        public Guid? GameId { get; }
        private string Name { get; }
        public override string ToString() => Name;
    }
}

public sealed record CapacityRuleRow(Guid RuleId, string Scope, string Period, int AllowedMinutes, bool Enabled);
