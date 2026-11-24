using System.Runtime.Versioning;
using Deckside.App.Ui;
using Deckside.Core.Directory;
using Deckside.Core.Settings;
using Deckside.Directory;
using static Deckside.App.Ui.Text;

namespace Deckside.App.Pages;

/// <summary>
/// The settings that matter for the scope. The master OU is the limit of everything: the other OUs must be
/// inside it (an OU outside it is cleared when saving) and every write is checked against it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class SettingsPage : UserControl
{
    private readonly AppState state;
    private readonly Action changed;
    private readonly ComboBox language = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
    private readonly TextBox server = new() { Width = 260 };
    private readonly NumericUpDown port = new() { Minimum = 0, Maximum = 65535, Width = 90 };
    private readonly CheckBox ldaps = new() { AutoSize = true };
    private readonly TextBox domain = new() { Width = 260 };
    private readonly TextBox masterOu = new() { Width = 560 };
    private readonly TextBox usersOu = new() { Width = 560 };
    private readonly TextBox computersOu = new() { Width = 560 };
    private readonly TextBox disabledOu = new() { Width = 560 };
    private readonly CheckBox subOus = new() { AutoSize = true };
    private readonly CheckBox ticket = new() { AutoSize = true };
    private readonly Label result = new() { AutoSize = true, MaximumSize = new Size(720, 0), Margin = new Padding(0, 8, 0, 0) };

    public SettingsPage(AppState state, Action changed)
    {
        this.state = state;
        this.changed = changed;
        AutoScroll = true;
        language.Items.AddRange(["Español", "English"]);
        ldaps.Text = S("Use LDAPS (certificate)");
        subOus.Text = S("Include sub-OUs of the users and computers OUs");
        ticket.Text = S("A ticket is required for every change");
        Fill(state.Settings);

        var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true };
        void Row(string label, Control control, string? note = null)
        {
            grid.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(0, 6, 12, 6) });
            if (note is null)
            {
                grid.Controls.Add(control);
                return;
            }

            var cell = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false, Margin = Padding.Empty };
            cell.Controls.Add(control);
            cell.Controls.Add(Theme.Note(note));
            grid.Controls.Add(cell);
        }

        Row(S("Language"), language);
        Row(S("Server (IP or name)"), server, S("Used at the next start (the login window proposes it)."));
        Row(S("Port"), port);
        Row("", ldaps);
        Row(S("Domain"), domain, S("Empty = taken from the master OU."));
        Row(S("Master OU"), masterOu, S("Nothing is ever created, changed or moved outside this OU, including group memberships."));
        Row(S("Users OU"), usersOu);
        Row(S("Computers OU"), computersOu);
        Row(S("Disabled OU"), disabledOu);
        Row("", subOus);
        Row("", ticket);

        var save = Theme.PrimaryButton(S("Save"));
        save.Click += (_, _) => Save();
        var verify = new Button { Text = S("Check the master OU"), AutoSize = true };
        verify.Click += async (_, _) => await VerifyAsync();
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.AddRange([save, verify]);

        var layout = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(Theme.Heading(S("Settings")));
        layout.Controls.Add(Theme.Note(S("Credentials are never saved. The naming rules of users and computers will be configured here too.")));
        layout.Controls.Add(grid);
        layout.Controls.Add(buttons);
        layout.Controls.Add(result);
        Controls.Add(layout);
        ShowWarnings(state.Warnings, saved: false);
    }

    private void Fill(AppSettings s)
    {
        language.SelectedIndex = s.Language == "en" ? 1 : 0;
        server.Text = s.Server;
        port.Value = s.Port;
        ldaps.Checked = s.UseLdaps;
        domain.Text = s.Domain;
        masterOu.Text = s.MasterOU;
        usersOu.Text = s.UsersOU;
        computersOu.Text = s.ComputersOU;
        disabledOu.Text = s.DisabledOU;
        subOus.Checked = s.IncludeSubOUs;
        ticket.Checked = s.RequireTicket;
    }

    private void Save()
    {
        var updated = state.Settings with
        {
            Language = language.SelectedIndex == 1 ? "en" : "es",
            Server = server.Text.Trim(),
            Port = (int)port.Value,
            UseLdaps = ldaps.Checked,
            Domain = domain.Text.Trim(),
            MasterOU = masterOu.Text.Trim(),
            UsersOU = usersOu.Text.Trim(),
            ComputersOU = computersOu.Text.Trim(),
            DisabledOU = disabledOu.Text.Trim(),
            IncludeSubOUs = subOus.Checked,
            RequireTicket = ticket.Checked,
        };
        try
        {
            var warnings = state.Save(updated);
            Fill(state.Settings);
            ShowWarnings(warnings, saved: true);
            changed();
        }
        catch (IOException ex)
        {
            result.ForeColor = Theme.Danger;
            result.Text = F("Could not save the settings: {0}", ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            result.ForeColor = Theme.Danger;
            result.Text = F("Could not save the settings: {0}", ex.Message);
        }
    }

    private void ShowWarnings(IReadOnlyList<SettingsWarning> warnings, bool saved)
    {
        if (warnings.Count == 0)
        {
            result.ForeColor = Theme.Ok;
            result.Text = saved ? S("Saved.") : "";
            return;
        }

        result.ForeColor = Theme.Danger;
        result.Text = (saved ? S("Saved, with warnings:") : S("Warnings:")) + Environment.NewLine +
                      string.Join(Environment.NewLine, warnings.Select(w => "• " + Messages.ForWarning(w)));
    }

    private async Task VerifyAsync()
    {
        var dn = masterOu.Text.Trim();
        if (!DistinguishedName.IsValid(dn))
        {
            result.ForeColor = Theme.Danger;
            result.Text = S("The master OU is not a valid DN.");
            return;
        }

        try
        {
            var directory = state.Directory;
            var found = await Task.Run(() => directory.Get(dn, ["objectClass", "description"]));
            var nc = directory.Info.DefaultNamingContext;
            var inDomain = nc.Length == 0 || DistinguishedName.AreEqual(dn, nc) || DistinguishedName.IsDescendantOf(dn, nc);
            if (found is null || !inDomain)
            {
                result.ForeColor = Theme.Danger;
                result.Text = found is null ? Messages.ForCode("MASTER_OU_NOT_FOUND") : Messages.ForCode("MASTER_OU_NOT_IN_DOMAIN");
                return;
            }

            var count = await Task.Run(() => directory.Search(dn, "(|(objectClass=user)(objectClass=group))", SearchDepth.Subtree, ["objectClass"]).Count);
            result.ForeColor = Theme.Ok;
            result.Text = F("The master OU exists in {0} and holds {1} users, computers and groups.", directory.Info.DomainDns, count);
        }
        catch (DirectoryException ex)
        {
            result.ForeColor = Theme.Danger;
            result.Text = Messages.ForCode(ex.Code) + " " + ex.Detail;
        }
    }
}
