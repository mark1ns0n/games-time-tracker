using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace GameTimeTracker;

internal sealed class EaImportDialog : Form
{
    private readonly BindingList<EaImportRow> _rows;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false
    };

    public IReadOnlyList<GameProfile> SelectedGames { get; private set; } = [];

    public EaImportDialog(IReadOnlyList<EaGameCandidate> candidates)
    {
        _rows = new BindingList<EaImportRow>(candidates
            .Select(candidate => new EaImportRow(candidate))
            .ToList());

        Text = "Import EA games";
        Width = 920;
        Height = 520;
        MinimumSize = new Size(720, 380);
        StartPosition = FormStartPosition.CenterParent;

        ConfigureGrid();
        _grid.DataSource = _rows;

        var import = new Button { Text = "Import selected", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var selectAll = new Button { Text = "Select all", AutoSize = true };
        var selectNone = new Button { Text = "Select none", AutoSize = true };

        import.Click += (_, _) => SaveSelection();
        selectAll.Click += (_, _) => SetSelection(true);
        selectNone.Click += (_, _) => SetSelection(false);

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            Padding = new Padding(8),
            WrapContents = false
        };
        toolbar.Controls.Add(selectAll);
        toolbar.Controls.Add(selectNone);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(8)
        };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(import);

        Controls.Add(_grid);
        Controls.Add(toolbar);
        Controls.Add(buttons);
        AcceptButton = import;
        CancelButton = cancel;
    }

    private void ConfigureGrid()
    {
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = nameof(EaImportRow.Import),
            DataPropertyName = nameof(EaImportRow.Import),
            HeaderText = "Import",
            Width = 58
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(EaImportRow.Name),
            DataPropertyName = nameof(EaImportRow.Name),
            HeaderText = "Name",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 150
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(EaImportRow.Source),
            DataPropertyName = nameof(EaImportRow.Source),
            HeaderText = "Source",
            Width = 90,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(EaImportRow.ProcessName),
            DataPropertyName = nameof(EaImportRow.ProcessName),
            HeaderText = "Process",
            Width = 150
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(EaImportRow.ExecutablePath),
            DataPropertyName = nameof(EaImportRow.ExecutablePath),
            HeaderText = "Executable",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 190
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(EaImportRow.InstallDirectory),
            DataPropertyName = nameof(EaImportRow.InstallDirectory),
            HeaderText = "Install directory",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 190,
            ReadOnly = true
        });
    }

    private void SetSelection(bool selected)
    {
        foreach (var row in _rows)
        {
            row.Import = selected && !string.IsNullOrWhiteSpace(row.ProcessName);
        }

        _grid.Refresh();
    }

    private void SaveSelection()
    {
        _grid.EndEdit();
        var games = _rows
            .Where(row => row.Import)
            .Select(row => row.ToGameProfile())
            .ToList();

        if (games.Count == 0)
        {
            MessageBox.Show(this, "Select at least one EA game with a process name.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        if (games.Any(game => game.ProcessNames.Count == 0))
        {
            MessageBox.Show(this, "Every selected game needs a process name.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        SelectedGames = games;
    }

    private sealed class EaImportRow
    {
        private static readonly Size CachedIconSize = new(128, 128);

        private readonly EaGameCandidate _candidate;

        public EaImportRow(EaGameCandidate candidate)
        {
            _candidate = candidate;
            Import = !string.IsNullOrWhiteSpace(candidate.ProcessName);
            Name = candidate.Name;
            Source = candidate.Source;
            ProcessName = candidate.ProcessName;
            ExecutablePath = candidate.ExecutablePath ?? "";
            InstallDirectory = candidate.InstallDirectory;
        }

        public bool Import { get; set; }
        public string Name { get; set; }
        public string Source { get; }
        public string ProcessName { get; set; }
        public string ExecutablePath { get; set; }
        public string InstallDirectory { get; }

        public GameProfile ToGameProfile()
        {
            var executablePath = string.IsNullOrWhiteSpace(ExecutablePath) ? null : ExecutablePath.Trim();
            return new GameProfile
            {
                DisplayName = string.IsNullOrWhiteSpace(Name) ? _candidate.Name : Name.Trim(),
                ExecutablePath = executablePath,
                ProcessNames = NormalizeProcessNames(ProcessName).ToList(),
                IconPath = IconImageLoader.CacheExecutableIcon(executablePath, CachedIconSize)
            };
        }

        private static IEnumerable<string> NormalizeProcessNames(string value)
        {
            return value
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(process => process.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? process : process + ".exe")
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }
    }
}
