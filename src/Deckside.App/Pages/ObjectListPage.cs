using System.Runtime.Versioning;
using Deckside.App.Ui;
using Deckside.Core.Directory;
using Deckside.Directory;
using static Deckside.App.Ui.Text;

namespace Deckside.App.Pages;

/// <summary>
/// Read-only list of the users or computers of their OU, with a quick filter. Creating comes in a next phase,
/// through the planners (preview + confirmation) and the guarded directory.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ObjectListPage : Page
{
    public enum Kind
    {
        Users,
        Computers,
    }

    private static readonly string[] Attributes = ["sAMAccountName", "displayName", "cn", "userAccountControl", "lastLogonTimestamp", "operatingSystem", "description"];

    private readonly Kind kind;
    private readonly DataGridView grid = Make.Grid();
    private readonly TextBox filter = Make.Input(width: 320);
    private readonly Label count = Make.Note("");
    private readonly Banner notice = new();
    private readonly Button refresh;
    private List<string[]> rows = [];
    private bool loaded;

    public ObjectListPage(MainForm shell, Kind kind)
        : base(shell, kind == Kind.Users ? S("Users") : S("Computers"), "")
    {
        this.kind = kind;
        filter.PlaceholderText = S("Filter by account, name or description...");
        filter.TextChanged += (_, _) => Fill();

        grid.Columns.Add("sam", S("Account"));
        grid.Columns.Add("name", S("Name"));
        grid.Columns.Add("state", S("State"));
        grid.Columns.Add("logon", S("Last logon"));
        grid.Columns.Add("extra", kind == Kind.Users ? S("Description") : S("Operating system"));
        grid.Columns.Add("dn", "DN");
        grid.Columns["sam"]!.FillWeight = 60;
        grid.Columns["state"]!.FillWeight = 45;
        grid.Columns["logon"]!.FillWeight = 50;
        grid.Columns["dn"]!.FillWeight = 160;
        grid.CellFormatting += (_, e) =>
        {
            if (e.ColumnIndex == 2 && e.Value is string s && e.CellStyle is not null)
            {
                e.CellStyle.ForeColor = s == S("Disabled") ? Theme.Muted : Theme.Success;
            }
        };

        var top = new Panel { Dock = DockStyle.Top, Height = 44, BackColor = Theme.Background };
        var topRow = Make.Row(filter, count);
        count.Margin = new Padding(12, 8, 0, 0);
        topRow.Dock = DockStyle.Fill;
        top.Controls.Add(topRow);
        var frame = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Border, Padding = new Padding(1) };
        frame.Controls.Add(grid);
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Background, Padding = new Padding(24, 16, 24, 16) };
        body.Controls.Add(frame);
        body.Controls.Add(notice);
        body.Controls.Add(top);
        SetBody(body);

        refresh = Action(Make.Secondary(S("Refresh"), async (_, _) => await LoadAsync()));
        var create = Action(Make.Primary(kind == Kind.Users ? S("New user...") : S("New computer...")));
        create.Enabled = false; // next phase
    }

    private string Ou => kind == Kind.Users ? App.Settings.UsersOU : App.Settings.ComputersOU;

    public override async void Activated()
    {
        SetDescription(Ou.Length > 0 ? F("Read only. OU: {0}", Ou) : S("The OU is not configured (or it is outside the master OU)."));
        notice.Show(Ou.Length > 0 ? null : S("Set the OU in Settings to see its objects."), Theme.WarningColor, Theme.WarningSoft);
        refresh.Enabled = Ou.Length > 0;
        if (!loaded && Ou.Length > 0)
        {
            await LoadAsync();
        }
    }

    protected override void UpdateButtons() => refresh.Enabled = Ou.Length > 0 && !Shell.Busy;

    private async Task LoadAsync()
    {
        var ou = Ou;
        if (ou.Length == 0)
        {
            return;
        }

        var filterText = kind == Kind.Users ? LdapFilter.Users : LdapFilter.Computers;
        var depth = App.Settings.IncludeSubOUs ? SearchDepth.Subtree : SearchDepth.OneLevel;
        var directory = App.Directory;
        var objects = await Shell.RunAsync(S("Reading the directory..."), token => directory.Search(ou, filterText, depth, Attributes, token));
        if (objects is null || IsDisposed)
        {
            return;
        }

        loaded = true;
        rows = [.. objects.OrderBy(o => o.SamAccountName, StringComparer.OrdinalIgnoreCase).Select(o => new[]
        {
            o.SamAccountName ?? "",
            o.Get("displayName") ?? o.Get("cn") ?? "",
            o.AccountControl.HasFlag(UserAccountControl.AccountDisable) ? S("Disabled") : S("Enabled"),
            AdTime.FromFileTime(o.Get("lastLogonTimestamp"))?.ToLocalTime().ToString("yyyy-MM-dd", Culture) ?? "—",
            (kind == Kind.Users ? o.Get("description") : o.Get("operatingSystem")) ?? "",
            o.Dn,
        })];
        Fill();
        Shell.SetStatus(F("{0} objects read from {1}.", rows.Count, ou));
    }

    private void Fill()
    {
        var text = filter.Text.Trim();
        grid.SuspendLayout();
        grid.Rows.Clear();
        var shown = 0;
        foreach (var r in rows)
        {
            if (text.Length == 0 || r.Take(5).Any(v => v.Contains(text, StringComparison.OrdinalIgnoreCase)))
            {
                grid.Rows.Add(r);
                shown++;
            }
        }

        grid.ResumeLayout();
        count.Text = text.Length == 0 ? F("{0} objects", rows.Count) : F("{0} of {1} objects", shown, rows.Count);
    }
}
