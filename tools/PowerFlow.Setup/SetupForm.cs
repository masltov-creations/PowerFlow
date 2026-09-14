using System.Drawing;

namespace PowerFlow.Setup;

internal sealed class SetupForm : Form
{
    private readonly CheckBox _desktop = new() { Text = "Create a desktop shortcut", Checked = true, AutoSize = true };
    private readonly CheckBox _launch = new() { Text = "Launch PowerFlow when installation finishes", Checked = true, AutoSize = true };
    private readonly Label _status = new() { Text = "Ready to install.", AutoSize = false, Height = 36, Dock = DockStyle.Fill };
    private readonly Button _install = new() { Text = Directory.Exists(InstallerEngine.InstallRoot) ? "Update PowerFlow" : "Install PowerFlow", AutoSize = true, Height = 36 };
    private readonly Button _cancel = new() { Text = "Cancel", AutoSize = true, Height = 36, DialogResult = DialogResult.Cancel };

    internal SetupForm()
    {
        Text = "PowerFlow Setup";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = true;
        ClientSize = new Size(540, 292);
        Font = new Font("Segoe UI", 10f);

        var heading = new Label
        {
            Text = Directory.Exists(InstallerEngine.InstallRoot) ? "Update PowerFlow" : "Install PowerFlow",
            Font = new Font(Font.FontFamily, 18f, FontStyle.Bold),
            AutoSize = true,
            Margin = new Padding(0, 0, 0, 6)
        };
        var description = new Label
        {
            Text = "PowerFlow will be installed for your Windows account. No administrator access is required. Your existing PowerFlow settings are preserved when you update or uninstall.",
            AutoSize = false,
            Height = 62,
            Dock = DockStyle.Fill
        };

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
        buttons.Controls.Add(_install);
        buttons.Controls.Add(_cancel);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(24, 20, 24, 18),
            ColumnCount = 1,
            RowCount = 7
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 10));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(description, 0, 1);
        layout.Controls.Add(_desktop, 0, 2);
        layout.Controls.Add(_launch, 0, 3);
        layout.Controls.Add(_status, 0, 5);
        layout.Controls.Add(buttons, 0, 6);
        Controls.Add(layout);

        AcceptButton = _install;
        CancelButton = _cancel;
        _install.Click += InstallClicked;
    }

    private async void InstallClicked(object? sender, EventArgs e)
    {
        SetBusy(true);
        try
        {
            var progress = new Progress<string>(message => _status.Text = message);
            await InstallerEngine.InstallAsync(_desktop.Checked, _launch.Checked, progress);
            MessageBox.Show("PowerFlow is installed and ready to use.", "PowerFlow", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = "Installation did not complete.";
            MessageBox.Show(ex.Message, "PowerFlow Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _desktop.Enabled = !busy;
        _launch.Enabled = !busy;
        _install.Enabled = !busy;
        _cancel.Enabled = !busy;
        UseWaitCursor = busy;
    }
}
