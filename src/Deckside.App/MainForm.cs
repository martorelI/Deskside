using System.Runtime.Versioning;
using Deckside.App.Pages;
using Deckside.App.Ui;
using static Deckside.App.Ui.Text;

namespace Deckside.App;

/// <summary>Sidebar with the sections, a connection bar on top and the current page.</summary>
[SupportedOSPlatform("windows")]
internal sealed class MainForm : Form
{
    private readonly AppState state;
    private readonly Panel content = new() { Dock = DockStyle.Fill, Padding = new Padding(24), BackColor = Theme.Background, AutoScroll = true };
    private readonly Label connection = new() { Dock = DockStyle.Top, AutoSize = false, Height = 30, Padding = new Padding(12, 7, 12, 0), BackColor = Color.White };
    private readonly Label blocked = new() { Dock = DockStyle.Top, AutoSize = false, Height = 30, Padding = new Padding(12, 7, 12, 0), BackColor = Theme.DangerBack, ForeColor = Theme.Danger, Visible = false };
    private readonly FlowLayoutPanel sidebar = new() { Dock = DockStyle.Left, Width = 200, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Sidebar, Padding = new Padding(0, 12, 0, 0) };
    private Button? active;

    public MainForm(AppState state)
    {
        this.state = state;
        Text = Deckside.Core.AppInfo.Name;
        Font = Theme.Body;
        MinimumSize = new Size(1000, 640);
        StartPosition = FormStartPosition.CenterScreen;

        AddSection(S("Users"), () => new ObjectListPage(state, ObjectListPage.Kind.Users));
        AddSection(S("Computers"), () => new ObjectListPage(state, ObjectListPage.Kind.Computers));
        AddSection(S("Offboarding"), () => new PlannedPage(S("Offboarding"), S("Find an employee, check whether the account is still active, then disable it and move it to the disabled OU. Every change is previewed, confirmed and recorded.")));
        AddSection(S("Health report"), () => new PlannedPage(S("Health report"), S("Expired, inactive, locked-out objects and password problems of the users and computers OUs, with Excel export. Read only.")));
        AddSection(S("Profiles"), () => new PlannedPage(S("Profiles"), S("Group templates to assign or change on users and computers. Only groups of the profiles inside the master OU are changed.")));
        AddSection(S("Settings"), () => new SettingsPage(state, RefreshConnectionBar));
        AddSection(S("About"), () => new AboutPage());

        Controls.Add(content);
        Controls.Add(blocked);
        Controls.Add(connection);
        Controls.Add(sidebar);
        RefreshConnectionBar();
        ((Button)sidebar.Controls[state.Directory.WriteBlockedReason() is null ? 0 : 5]).PerformClick();
    }

    private void AddSection(string title, Func<Control> page)
    {
        var button = new Button
        {
            Text = title, Width = 200, Height = 40, FlatStyle = FlatStyle.Flat, ForeColor = Theme.SidebarText, BackColor = Theme.Sidebar,
            TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(16, 0, 0, 0), Margin = Padding.Empty,
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += (_, _) =>
        {
            if (active is not null)
            {
                active.BackColor = Theme.Sidebar;
            }

            active = button;
            button.BackColor = Theme.SidebarActive;
            foreach (Control c in content.Controls)
            {
                c.Dispose();
            }

            content.Controls.Clear();
            var control = page();
            control.Dock = DockStyle.Fill;
            content.Controls.Add(control);
        };
        sidebar.Controls.Add(button);
    }

    private void RefreshConnectionBar()
    {
        var info = state.Directory.Info;
        var dc = info.DcHostName.Length > 0 && !string.Equals(info.DcHostName, info.Server, StringComparison.OrdinalIgnoreCase) ? " (" + info.DcHostName + ")" : "";
        var who = info.BoundAs.Length > 0 ? info.BoundAs : S("my Windows session");
        var master = state.Settings.MasterOU.Length > 0 ? state.Settings.MasterOU : S("not configured");
        connection.Text = F("Server: {0}{1}  ·  Account: {2}  ·  Encryption: {3}  ·  Master OU: {4}", info.Server, dc, who, Messages.Encryption(info.Encryption), master);
        var reason = state.Directory.WriteBlockedReason();
        blocked.Visible = reason is not null;
        blocked.Text = reason is null ? "" : Messages.ForCode(reason);
    }
}
