using System.ComponentModel;
using System.Drawing;
using System.Windows.Forms;

namespace GameTimeTracker;

internal sealed class SteamImportDialog : Form
{
    private readonly BindingList<SteamImportRow> _rows;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        AutoGenerateColumns = false,
        AllowUserToAddRows = false
    };

    public IReadOnlyList<GameProfile> SelectedGames { get; private set; } = [];

    public SteamImportDialog(IReadOnlyList<SteamGameCandidate> candidates)
    {
        _rows = new BindingList<SteamImportRow>(candidates
            .Select(candidate => new SteamImportRow(candidate))
            .ToList());

        Text = "Import Steam games";
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
            Name = nameof(SteamImportRow.Import),
            DataPropertyName = nameof(SteamImportRow.Import),
            HeaderText = "Import",
            Width = 58
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(SteamImportRow.Name),
            DataPropertyName = nameof(SteamImportRow.Name),
            HeaderText = "Name",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 150
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(SteamImportRow.AppId),
            DataPropertyName = nameof(SteamImportRow.AppId),
            HeaderText = "App ID",
            Width = 80,
            ReadOnly = true
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(SteamImportRow.ProcessName),
            DataPropertyName = nameof(SteamImportRow.ProcessName),
            HeaderText = "Process",
            Width = 150
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(SteamImportRow.ExecutablePath),
            DataPropertyName = nameof(SteamImportRow.ExecutablePath),
            HeaderText = "Executable",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            FillWeight = 190
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = nameof(SteamImportRow.InstallDirectory),
            DataPropertyName = nameof(SteamImportRow.InstallDirectory),
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
            MessageBox.Show(this, "Select at least one Steam game with a process name.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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

    private sealed class SteamImportRow
    {
        private static readonly Size CachedIconSize = new(128, 128);

        private readonly SteamGameCandidate _candidate;

        public SteamImportRow(SteamGameCandidate candidate)
        {
            _candidate = candidate;
            Import = !string.IsNullOrWhiteSpace(candidate.ProcessName);
            Name = candidate.Name;
            AppId = candidate.AppId;
            ProcessName = candidate.ProcessName;
            ExecutablePath = candidate.ExecutablePath ?? "";
            InstallDirectory = candidate.InstallDirectory;
        }

        public bool Import { get; set; }
        public string Name { get; set; }
        public string AppId { get; }
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
