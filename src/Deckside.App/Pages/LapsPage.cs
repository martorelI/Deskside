using System.Runtime.Versioning;
using Deckside.App.Ui;
using Deckside.Core.Directory;
using Deckside.Core.Laps;
using Deckside.Directory;
using static Deckside.App.Ui.Text;

namespace Deckside.App.Pages;

/// <summary>
/// Read the LAPS password of a computer, as the standard tools do (Windows LAPS, encrypted or not, and
/// legacy LAPS). Read only. The password is shown hidden, never logged or saved, cleared from the clipboard
/// after a while and from the page when the operator leaves it.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class LapsPage : Page
{
    private const int ClipboardSeconds = 30;

    private readonly TextBox computer = Make.Input(width: 300);
    private readonly Button get;
    private readonly Card result = new(S("Result"));
    private readonly Banner banner = new();
    private readonly Label foundDn = Make.Label("");
    private readonly Label account = Make.Label("");
    private readonly TextBox password = new()
    {
        ReadOnly = true, UseSystemPasswordChar = true, Font = Theme.MonoLarge, Width = 360, BorderStyle = BorderStyle.FixedSingle,
        BackColor = Theme.Background, Margin = new Padding(0, 2, 8, 6),
    };
    private readonly Button show;
    private readonly Button copy;
    private readonly Label source = Make.Label("");
    private readonly Label updated = Make.Label("");
    private readonly Label expires = Make.Label("");
    private readonly System.Windows.Forms.Timer clipboardTimer = new() { Interval = ClipboardSeconds * 1000 };
    private string copied = "";

    public LapsPage(MainForm shell)
        : base(shell, "LAPS", S("Local administrator password of a computer (Windows LAPS and legacy LAPS). Read only."))
    {
        var list = new CardList();
        var search = list.Add(new Card(S("Computer"), S("Name, NAME$ or full DNS name. It is searched in the whole domain; AD only returns the password if your account may read it.")));
        get = Make.Primary(S("Get password"), async (_, _) => await GetAsync());
        get.Margin = new Padding(8, 0, 0, 4);
        computer.PlaceholderText = "PC01";
        computer.KeyDown += async (_, e) =>
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                await GetAsync();
            }
        };
        search.Add(Make.Row(computer, get));

        show = Make.Secondary(S("Show"), (_, _) => Reveal(password.UseSystemPasswordChar));
        copy = Make.Secondary(S("Copy"), (_, _) => Copy());
        var fields = new Fields(150);
        fields.Add(S("Computer"), foundDn);
        fields.Add(S("Account"), account);
        fields.Add(S("Password"), Make.Row(password, show, copy));
        fields.Add(S("Source"), source);
        fields.Add(S("Last change"), updated);
        fields.Add(S("Expires"), expires);
        result.Add(fields);
        result.Add(Make.Note(S("Encrypted Windows LAPS passwords are decrypted with your Windows session (not the account typed at login): it must be an authorized decryptor.")));
        result.Visible = false;
        list.Add(result);

        var body = new Panel { Dock = DockStyle.Fill };
        body.Controls.Add(list);
        body.Controls.Add(banner);
        SetBody(body);

        clipboardTimer.Tick += (_, _) => ClearClipboard();
    }

    public override void Activated() => computer.Focus();

    /// <summary>Leaving the page forgets the password shown on it.</summary>
    public override void Deactivated() => Forget();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            ClearClipboard();
            clipboardTimer.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task GetAsync()
    {
        Forget();
        var name = LapsReader.NormalizeComputerName(computer.Text);
        if (name.Length == 0)
        {
            banner.Show("⚠  " + S("Type a valid computer name (up to 15 letters, digits or '-')."), Theme.WarningColor, Theme.WarningSoft);
            return;
        }

        var directory = App.Directory;
        var nc = directory.Info.DefaultNamingContext;
        var filter = LdapFilter.And(LdapFilter.Computers, LdapFilter.Equal("sAMAccountName", name + "$"));
        // Null means the query failed or was stopped (RunAsync already told the operator).
        var found = await Shell.RunAsync(F("Looking for {0}...", name), token =>
        {
            var objects = directory.Search(nc, filter, SearchDepth.Subtree, LapsReader.Attributes, token);
            return objects.Count == 0 ? new Lookup(null, null) : new Lookup(objects[0], LapsReader.Read(objects[0], LapsDecryptor.Decrypt));
        });
        if (found is null || IsDisposed)
        {
            return;
        }

        if (found.Computer is null || found.Laps is null)
        {
            banner.Show("⚠  " + F("No computer named {0} was found in {1}.", name, directory.Info.DomainDns), Theme.WarningColor, Theme.WarningSoft);
            Shell.SetStatus(F("{0} not found.", name));
            return;
        }

        var (obj, laps) = (found.Computer, found.Laps);
        foundDn.Text = obj.Get("dNSHostName") is { Length: > 0 } host ? host + "  ·  " + obj.Dn : obj.Dn;
        expires.Text = When(laps.ExpiresUtc, future: true);
        if (!laps.Found)
        {
            var message = laps.Status switch
            {
                "DECRYPT_FAILED" => S("The password is encrypted and your Windows session cannot decrypt it (it is not an authorized decryptor)."),
                "INVALID_DATA" => S("The LAPS value of this computer is not valid."),
                _ => S("No LAPS password: the computer does not use LAPS, or your account may not read it."),
            };
            banner.Show("⚠  " + message, Theme.Danger, Theme.DangerSoft);
            Shell.SetStatus(F("{0}: no password.", name));
            return;
        }

        account.Text = laps.Account.Length > 0 ? laps.Account : "—";
        password.Text = laps.Password;
        Reveal(false);
        source.Text = laps.Source switch
        {
            LapsSources.WindowsLapsEncrypted => S("Windows LAPS (encrypted)"),
            LapsSources.WindowsLaps => S("Windows LAPS"),
            _ => S("Legacy LAPS"),
        };
        updated.Text = When(laps.UpdatedUtc, future: false);
        result.Visible = true;
        banner.Show("✓  " + F("Password of {0} read.", name), Theme.Success, Theme.SuccessSoft);
        Shell.SetStatus(F("Password of {0} read. It is not saved anywhere.", name));
    }

    private sealed record Lookup(DirectoryObject? Computer, LapsResult? Laps);

    private static string When(DateTimeOffset? utc, bool future)
    {
        if (utc is null)
        {
            return "—";
        }

        var local = utc.Value.ToLocalTime();
        var days = (int)Math.Round((utc.Value - DateTimeOffset.UtcNow).TotalDays);
        var relative = !future ? F("{0} days ago", -days)
            : days < 0 ? S("already expired")
            : F("in {0} days", days);
        return local.ToString("yyyy-MM-dd HH:mm", Culture) + "   (" + relative + ")";
    }

    private void Reveal(bool visible)
    {
        password.UseSystemPasswordChar = !visible;
        show.Text = visible ? S("Hide") : S("Show");
    }

    private void Copy()
    {
        if (password.Text.Length == 0)
        {
            return;
        }

        Clipboard.SetText(password.Text);
        copied = password.Text;
        clipboardTimer.Stop();
        clipboardTimer.Start();
        Shell.SetStatus(F("Password copied. The clipboard is cleared in {0} seconds.", ClipboardSeconds));
    }

    /// <summary>Clears the clipboard only if it still holds the password copied from here.</summary>
    private void ClearClipboard()
    {
        clipboardTimer.Stop();
        if (copied.Length == 0)
        {
            return;
        }

        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == copied)
            {
                Clipboard.Clear();
            }
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
        }

        copied = "";
    }

    private void Forget()
    {
        password.Text = "";
        Reveal(false);
        result.Visible = false;
        banner.Show(null, Theme.Muted, Theme.Background);
    }
}
