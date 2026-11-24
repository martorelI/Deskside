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
    private readonly TextBox server = Make.Input();
    private readonly NumericUpDown port = Make.Number(0, 0, 65535);
    private readonly CheckBox ldaps = Make.Check(S("Use LDAPS (certificate)"));
    private readonly RadioButton otherAccount = Make.Radio(S("Another account"), true);
    private readonly RadioButton windowsSession = Make.Radio(S("My Windows session"));
    private readonly TextBox user = Make.Input();
    private readonly TextBox password = Make.Input(password: true);
    private readonly CheckBox remember = Make.Check(S("Remember the server"), true);
    private readonly Label hint = Make.Note("");
    private readonly Banner error = new();
    private readonly Button connect;

    public LoginForm(AppState state)
    {
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        this.state = state;
        Text = Deckside.Core.AppInfo.Name + " " + Deckside.Core.AppInfo.Version;
        Font = Theme.Body;
        BackColor = Theme.Surface;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(560, 520);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (ArgumentException)
        {
        }

        server.Text = state.Settings.Server;
        port.Value = Math.Clamp(state.Settings.Port, 0, 65535);
        ldaps.Checked = state.Settings.UseLdaps;
        user.PlaceholderText = @"DOMAIN\user  or  user@domain";

        // Brand band, same as the sidebar of the main window.
        var band = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Sidebar, Padding = new Padding(22, 22, 12, 18) };
        var logo = new PictureBox { Image = Theme.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(48, 48), Dock = DockStyle.Left };
        var titles = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12, 0, 0, 0) };
        var name = new Label { Text = Deckside.Core.AppInfo.Name, ForeColor = Color.White, Font = Theme.Brand, Dock = DockStyle.Top, Height = 28 };
        var sub = new Label { Text = S("Connect to the domain"), ForeColor = Theme.SidebarText, Font = Theme.Body, Dock = DockStyle.Top, Height = 22 };
        titles.Controls.Add(sub);
        titles.Controls.Add(name);
        band.Controls.Add(titles);
        band.Controls.Add(logo);

        var fields = new Fields(170);
        fields.Add(S("Server (IP or name)"), server);
        fields.Add(S("Port"), Make.Row(port, Make.Note(S("0 = default (389 / 636)"))));
        fields.Add("", ldaps);
        fields.Add(S("Authenticate with"), Make.Row(otherAccount, windowsSession));
        fields.Add(S("Account"), user);
        fields.Add(S("Password"), password);
        fields.Add("", remember);
        fields.Full(hint);
        var middle = new Panel { Dock = DockStyle.Fill, Padding = new Padding(24, 18, 24, 8) };
        middle.Controls.Add(fields);

        connect = Make.Primary(S("Connect"), async (_, _) => await ConnectAsync());
        var exit = Make.Secondary(S("Exit"), (_, _) => DialogResult = DialogResult.Cancel);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(16, 12, 16, 12), BackColor = Theme.Background };
        buttons.Controls.Add(connect);
        buttons.Controls.Add(exit);
        var line = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border };

        Controls.Add(middle);
        Controls.Add(error);
        Controls.Add(line);
        Controls.Add(buttons);
        Controls.Add(band);
        AcceptButton = connect;
        CancelButton = exit;

        windowsSession.CheckedChanged += (_, _) => UpdateState();
        server.TextChanged += (_, _) => UpdateState();
        ldaps.CheckedChanged += (_, _) => UpdateState();
        Shown += (_, _) => (server.Text.Length == 0 ? server : user.Text.Length == 0 ? user : password).Focus();
        UpdateState();
    }

    private void UpdateState()
    {
        user.Enabled = password.Enabled = otherAccount.Checked;
        var isIp = ServerAddress.IsIpAddress(server.Text.Trim());
        hint.Text = !isIp ? S("The connection is always encrypted: LDAPS, or signing and sealing on port 389.")
            : ldaps.Checked ? S("With LDAPS and an IP address, the certificate of the DC must include that IP.")
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
        UseWaitCursor = true;
        error.Show(F("Connecting to {0}...", options.Server), Theme.Accent, Theme.AccentSoft);
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
            error.Show("⚠  " + Messages.ForCode(ex.Code), Theme.Danger, Theme.DangerSoft);
            if (ex.Code is "CONNECTION_FAILED")
            {
                hint.Text = ex.Detail;
            }
        }
        finally
        {
            UseWaitCursor = false;
            connect.Enabled = true;
        }
    }
}
