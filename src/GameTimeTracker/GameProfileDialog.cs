using System.Windows.Forms;

using System.Drawing;

namespace GameTimeTracker;

public sealed class GameProfileDialog : Form
{
    private static readonly Size PreviewIconSize = new(32, 32);
    private static readonly Size CachedIconSize = new(128, 128);

    private readonly TextBox _name = new() { Width = 320 };
    private readonly TextBox _processes = new() { Width = 320 };
    private readonly TextBox _icon = new() { Width = 236 };
    private readonly PictureBox _iconPreview = new()
    {
        Width = 32,
        Height = 32,
        SizeMode = PictureBoxSizeMode.Zoom
    };

    public GameProfile Game { get; private set; } = new();

    public GameProfileDialog()
    {
        Text = "Add game";
        Width = 540;
        Height = 260;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        var browseIcon = new Button { Text = "Browse...", AutoSize = true };
        ok.Click += (_, _) => SaveGame();
        browseIcon.Click += (_, _) => BrowseIcon();
        _icon.TextChanged += (_, _) => RefreshIconPreview();

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(12),
            ColumnCount = 2,
            RowCount = 5,
            AutoSize = true
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));

        layout.Controls.Add(new Label { Text = "Name", AutoSize = true }, 0, 0);
        layout.Controls.Add(_name, 1, 0);
        layout.Controls.Add(new Label { Text = "Processes", AutoSize = true }, 0, 1);
        layout.Controls.Add(_processes, 1, 1);
        layout.Controls.Add(new Label { Text = "Icon path", AutoSize = true }, 0, 2);
        layout.Controls.Add(CreateIconPicker(browseIcon), 1, 2);
        layout.Controls.Add(new Label { Text = "Example: bg3.exe, bg3_dx11.exe", AutoSize = true }, 1, 3);

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(ok);
        layout.Controls.Add(buttons, 1, 4);

        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(layout);
        RefreshIconPreview();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _iconPreview.Image?.Dispose();
        }

        base.Dispose(disposing);
    }

    private Control CreateIconPicker(Button browseIcon)
    {
        var picker = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };

        picker.Controls.Add(_icon);
        picker.Controls.Add(browseIcon);
        picker.Controls.Add(_iconPreview);
        return picker;
    }

    private void BrowseIcon()
    {
        using var dialog = new OpenFileDialog
        {
            Title = "Choose game icon",
            Filter = "Icon sources (*.ico;*.png;*.exe)|*.ico;*.png;*.exe|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _icon.Text = CacheIconSource(dialog.FileName);
        }
    }

    private void RefreshIconPreview()
    {
        var previous = _iconPreview.Image;
        _iconPreview.Image = IconImageLoader.LoadBitmap(_icon.Text, PreviewIconSize)
            ?? IconImageLoader.CreateFallback(PreviewIconSize);
        previous?.Dispose();
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
            IconPath = string.IsNullOrWhiteSpace(_icon.Text) ? null : CacheIconSource(_icon.Text)
        };
    }

    private static string CacheIconSource(string sourcePath)
    {
        var trimmed = sourcePath.Trim();
        return IconImageLoader.CacheExecutableIcon(trimmed, CachedIconSize) ?? trimmed;
    }

    private static string NormalizeProcessName(string value)
    {
        var trimmed = value.Trim();
        return trimmed.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? trimmed : trimmed + ".exe";
    }
}

