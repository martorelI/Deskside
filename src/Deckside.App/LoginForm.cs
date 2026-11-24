using System.Runtime.Versioning;
using Deckside.App.Ui;
using Deckside.Core.Settings;
using Deckside.Directory;
using static Deckside.App.Ui.Text;

namespace Deckside.App;

/// <summary>
/// First window: where to send the requests (IP preferred) and with which account. The password stays in
/// memory for this session only; only the server can be remembered in settings.json.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class LoginForm : Form
{
    private readonly AppState state;
    private readonly TextBox server = new() { Width = 260 };
    private readonly NumericUpDown port = new() { Minimum = 0, Maximum = 65535, Width = 90 };
    private readonly CheckBox ldaps = new() { AutoSize = true };
    private readonly RadioButton otherAccount = new() { AutoSize = true, Checked = true };
    private readonly RadioButton windowsSession = new() { AutoSize = true };
    private readonly TextBox user = new() { Width = 260, PlaceholderText = @"DOMINIO\usuario  /  usuario@dominio" };
    private readonly TextBox password = new() { Width = 260, UseSystemPasswordChar = true };
    private readonly CheckBox remember = new() { AutoSize = true, Checked = true };
    private readonly Label hint = Theme.Note("");
    private readonly Label status = new() { AutoSize = true, MaximumSize = new Size(420, 0) };
    private readonly Button connect = Theme.PrimaryButton("");
    private readonly Button exit = new() { AutoSize = true };

    public LoginForm(AppState state)
    {
        this.state = state;
        Text = AppInfoText();
        Font = Theme.Body;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        server.Text = state.Settings.Server;
        port.Value = state.Settings.Port;
        ldaps.Checked = state.Settings.UseLdaps;
        ldaps.Text = S("Use LDAPS (certificate)");
        otherAccount.Text = S("Another account");
        windowsSession.Text = S("My Windows session");
        remember.Text = S("Remember the server");
        connect.Text = S("Connect");
        exit.Text = S("Exit");
        exit.Click += (_, _) => DialogResult = DialogResult.Cancel;
        connect.Click += async (_, _) => await ConnectAsync();
        windowsSession.CheckedChanged += (_, _) => UpdateState();
        server.TextChanged += (_, _) => UpdateState();
        ldaps.CheckedChanged += (_, _) => UpdateState();

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
        void Row(string label, Control control)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) });
            grid.Controls.Add(control);
        }

        var portRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        portRow.Controls.AddRange([port, new Label { Text = S("0 = default (389 / 636)"), AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(6, 6, 0, 0) }]);
        var accountRow = new FlowLayoutPanel { AutoSize = true, Margin = Padding.Empty };
        accountRow.Controls.AddRange([otherAccount, windowsSession]);
        Row(S("Server (IP or name)"), server);
        Row(S("Port"), portRow);
        Row("", ldaps);
        Row(S("Authenticate with"), accountRow);
        Row(S("Account"), user);
        Row(S("Password"), password);
        Row("", remember);

        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        buttons.Controls.AddRange([connect, exit]);

        var layout = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        layout.Controls.Add(Theme.Heading(S("Connect to the domain")));
        layout.Controls.Add(grid);
        layout.Controls.Add(hint);
        layout.Controls.Add(status);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        AcceptButton = connect;
        CancelButton = exit;
        UpdateState();
    }

    private static string AppInfoText() => Deckside.Core.AppInfo.Name + " " + Deckside.Core.AppInfo.Version;

    private void UpdateState()
    {
        user.Enabled = password.Enabled = otherAccount.Checked;
        var isIp = ServerAddress.IsIpAddress(server.Text.Trim());
        hint.Text = !isIp ? "" : ldaps.Checked
            ? S("With LDAPS and an IP address, the certificate of the DC must include that IP.")
            : S("With an IP address Windows usually authenticates with NTLM instead of Kerberos; the connection is still signed and sealed.");
    }

    private async Task ConnectAsync()
    {
        var options = new ConnectionOptions
        {
            Server = server.Text.Trim(),
            Port = (int)port.Value,
            UseLdaps = ldaps.Checked,
            UseCurrentWindowsSession = windowsSession.Checked,
            UserName = user.Text,
            Password = password.Text,
        };
        connect.Enabled = false;
        status.ForeColor = Theme.Muted;
        status.Text = F("Connecting to {0}...", options.Server);
        try
        {
            var directory = await Task.Run(() => LdapDirectory.Connect(options));
            password.Clear();
            state.Connected(directory);
            if (remember.Checked && (state.Settings.Server != options.Server || state.Settings.Port != options.Port || state.Settings.UseLdaps != options.UseLdaps))
            {
                state.Save(state.Settings with { Server = options.Server, Port = options.Port, UseLdaps = options.UseLdaps });
            }

            DialogResult = DialogResult.OK;
        }
        catch (DirectoryException ex)
        {
            status.ForeColor = Theme.Danger;
            status.Text = Messages.ForCode(ex.Code) + (ex.Code is "CONNECTION_FAILED" ? Environment.NewLine + ex.Detail : "");
        }
        finally
        {
            connect.Enabled = true;
        }
    }
}
