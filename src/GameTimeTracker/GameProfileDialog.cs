using System.Windows.Forms;

namespace GameTimeTracker;

public sealed class GameProfileDialog : Form
{
    private readonly TextBox _name = new() { Width = 320 };
    private readonly TextBox _processes = new() { Width = 320 };
    private readonly TextBox _icon = new() { Width = 320 };

    public GameProfile Game { get; private set; } = new();

    public GameProfileDialog()
    {
        Text = "Add game";
        Width = 460;
        Height = 240;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        ok.Click += (_, _) => SaveGame();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 5,
            AutoSize = true
        };

        layout.Controls.Add(new Label { Text = "Name", AutoSize = true }, 0, 0);
        layout.Controls.Add(_name, 1, 0);
        layout.Controls.Add(new Label { Text = "Processes", AutoSize = true }, 0, 1);
        layout.Controls.Add(_processes, 1, 1);
        layout.Controls.Add(new Label { Text = "Icon path", AutoSize = true }, 0, 2);
        layout.Controls.Add(_icon, 1, 2);
        layout.Controls.Add(new Label { Text = "Example: bg3.exe, bg3_dx11.exe", AutoSize = true }, 1, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 1, 4);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(layout);
    }

    private void SaveGame()
    {
        var processNames = _processes.Text
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeProcessName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (string.IsNullOrWhiteSpace(_name.Text) || processNames.Count == 0)
        {
            MessageBox.Show(this, "Name and at least one process are required.", "GameTimeTracker", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.None;
            return;
        }

        Game = new GameProfile
        {
            DisplayName = _name.Text.Trim(),
            ProcessNames = processNames,
            IconPath = string.IsNullOrWhiteSpace(_icon.Text) ? null : _icon.Text.Trim()
        };
    }

    private static string NormalizeProcessName(string value)
    {
        var trimmed = value.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".exe";
    }
}

