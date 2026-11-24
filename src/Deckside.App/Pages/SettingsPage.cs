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
internal sealed class SettingsPage : Page
{
    private readonly ComboBox language = Make.Combo(["English", "Español"], 0, 200);
    private readonly TextBox server = Make.Input();
    private readonly NumericUpDown port = Make.Number(0, 0, 65535);
    private readonly CheckBox ldaps = Make.Check(S("Use LDAPS (certificate)"));
    private readonly TextBox domain = Make.Input();
    private readonly TextBox masterOu = Make.Input(width: 560);
    private readonly TextBox usersOu = Make.Input(width: 560);
    private readonly TextBox computersOu = Make.Input(width: 560);
    private readonly TextBox disabledOu = Make.Input(width: 560);
    private readonly CheckBox subOus = Make.Check(S("Include sub-OUs of the users and computers OUs"));
    private readonly CheckBox ticket = Make.Check(S("A ticket is required for every change"));
    private readonly Banner banner = new();
    private readonly Label warnings = Make.Note("", Theme.WarningColor);
    private readonly Card warningsCard = new(S("Warnings"));

    public SettingsPage(MainForm shell)
        : base(shell, S("Settings"), S("Credentials are never saved. The naming rules of users and computers will be configured here too."))
    {
        var list = new CardList();

        warningsCard.Add(warnings);
        list.Add(warningsCard);

        var scope = list.Add(new Card(S("Scope"), S("Nothing is ever created, changed or moved outside the master OU, including group memberships. The other OUs must be inside it.")));
        var scopeFields = new Fields();
        scopeFields.Add(S("Master OU"), masterOu);
        scopeFields.Add(S("Users OU"), usersOu);
        scopeFields.Add(S("Computers OU"), computersOu);
        scopeFields.Add(S("Disabled OU"), disabledOu);
        scopeFields.Add("", subOus);
        scopeFields.Add("", ticket);
        scope.Add(scopeFields);
        masterOu.PlaceholderText = "OU=Managed,DC=example,DC=local";

        var connection = list.Add(new Card(S("Connection"), S("Used at the next start: the login window proposes it.")));
        var connectionFields = new Fields();
        connectionFields.Add(S("Server (IP or name)"), server);
        connectionFields.Add(S("Port"), Make.Row(port, Make.Note(S("0 = default (389 / 636)"))));
        connectionFields.Add("", ldaps);
        connectionFields.Add(S("Domain"), domain);
        connectionFields.Full(Make.Note(S("Domain: empty = taken from the master OU.")));
        connection.Add(connectionFields);

        var general = list.Add(new Card(S("General")));
        var generalFields = new Fields();
        generalFields.Add(S("Language"), language);
        generalFields.Full(Make.Note(S("The language changes at the next start.")));
        general.Add(generalFields);

        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(list);
        body.Controls.Add(banner);
        SetBody(body);

        Action(Make.Secondary(S("Check the master OU"), async (_, _) => await VerifyAsync()));
        Action(Make.Primary(S("Save"), (_, _) => Save()));
    }

    public override void Activated()
    {
        Fill(App.Settings);
        ShowWarnings(App.Warnings);
    }

    private void Fill(AppSettings s)
    {
        language.SelectedIndex = s.Language == "es" ? 1 : 0;
        server.Text = s.Server;
        port.Value = Math.Clamp(s.Port, 0, 65535);
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
        var updated = App.Settings with
        {
            Language = language.SelectedIndex == 1 ? "es" : "en",
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
            var result = App.Save(updated);
            Fill(App.Settings);
            ShowWarnings(result);
            banner.Show(result.Count == 0 ? "✓  " + S("Saved.") : "⚠  " + S("Saved, with warnings (see below)."),
                result.Count == 0 ? Theme.Success : Theme.WarningColor, result.Count == 0 ? Theme.SuccessSoft : Theme.WarningSoft);
            Shell.RefreshConnection();
            Shell.SetStatus(S("Settings saved."));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            banner.Show("⚠  " + F("Could not save the settings: {0}", ex.Message), Theme.Danger, Theme.DangerSoft);
        }
    }

    private void ShowWarnings(IReadOnlyList<SettingsWarning> items)
    {
        warnings.Text = string.Join(Environment.NewLine, items.Select(w => "•  " + Messages.ForWarning(w)));
        warningsCard.Visible = items.Count > 0;
    }

    private async Task VerifyAsync()
    {
        var dn = masterOu.Text.Trim();
        if (!DistinguishedName.IsValid(dn))
        {
            banner.Show("⚠  " + S("The master OU is not a valid DN."), Theme.Danger, Theme.DangerSoft);
            return;
        }

        var directory = App.Directory;
        var result = await Shell.RunAsync(S("Checking the master OU..."), token =>
        {
            var found = directory.Get(dn, ["objectClass"]);
            var count = found is null ? 0 : directory.Search(dn, "(|(objectClass=user)(objectClass=group))", SearchDepth.Subtree, ["objectClass"], token).Count;
            return new Check(found is not null, count);
        });
        if (result is null || IsDisposed)
        {
            return;
        }

        var nc = directory.Info.DefaultNamingContext;
        var inDomain = nc.Length == 0 || DistinguishedName.AreEqual(dn, nc) || DistinguishedName.IsDescendantOf(dn, nc);
        if (!result.Exists || !inDomain)
        {
            banner.Show("⚠  " + Messages.ForCode(result.Exists ? "MASTER_OU_NOT_IN_DOMAIN" : "MASTER_OU_NOT_FOUND"), Theme.Danger, Theme.DangerSoft);
            return;
        }

        banner.Show("✓  " + F("The master OU exists in {0} and holds {1} users, computers and groups.", directory.Info.DomainDns, result.Count), Theme.Success, Theme.SuccessSoft);
        Shell.SetStatus(S("Master OU checked."));
    }

    private sealed record Check(bool Exists, int Count);
}
