using System.Runtime.Versioning;
using Deckside.App.Ui;
using Deckside.Core.Directory;
using Deckside.Directory;
using static Deckside.App.Ui.Text;

namespace Deckside.App.Pages;

/// <summary>
/// Read-only list of the users or computers of their OU. Creating comes in phase 3, through the planners
/// (preview + confirmation) and the guarded directory.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class ObjectListPage : UserControl
{
    public enum Kind
    {
        Users,
        Computers,
    }

    private static readonly string[] Attributes = ["sAMAccountName", "displayName", "cn", "userAccountControl", "lastLogonTimestamp", "operatingSystem", "description"];

    private readonly AppState state;
    private readonly Kind kind;
    private readonly DataGridView grid = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, SelectionMode = DataGridViewSelectionMode.FullRowSelect, BackgroundColor = Color.White,
    };
    private readonly Label status = new() { AutoSize = true, ForeColor = Theme.Muted, Margin = new Padding(12, 8, 0, 0) };
    private CancellationTokenSource? loading;

    public ObjectListPage(AppState state, Kind kind)
    {
        this.state = state;
        this.kind = kind;
        var ou = kind == Kind.Users ? state.Settings.UsersOU : state.Settings.ComputersOU;

        var refresh = new Button { Text = S("Refresh"), AutoSize = true };
        refresh.Click += async (_, _) => await LoadAsync();
        var create = Theme.PrimaryButton(kind == Kind.Users ? S("New user...") : S("New computer..."));
        create.Enabled = false; // phase 3
        var bar = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(0, 0, 0, 8) };
        bar.Controls.AddRange([create, refresh, status]);

        var head = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        head.Controls.Add(Theme.Heading(kind == Kind.Users ? S("Users") : S("Computers")));
        head.Controls.Add(Theme.Note(ou.Length > 0 ? F("OU: {0}", ou) : S("The OU is not configured (or it is outside the master OU). Set it in Settings.")));

        grid.Columns.Add("sam", S("Account"));
        grid.Columns.Add("name", S("Name"));
        grid.Columns.Add("state", S("State"));
        grid.Columns.Add("logon", S("Last logon"));
        grid.Columns.Add("extra", kind == Kind.Users ? S("Description") : S("Operating system"));
        grid.Columns.Add("dn", "DN");

        Controls.Add(grid);
        Controls.Add(bar);
        Controls.Add(head);
        if (ou.Length > 0)
        {
            Load += async (_, _) => await LoadAsync();
        }
        else
        {
            refresh.Enabled = false;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            loading?.Cancel();
            loading?.Dispose();
        }

        base.Dispose(disposing);
    }

    private async Task LoadAsync()
    {
        loading?.Cancel();
        loading = new CancellationTokenSource();
        var token = loading.Token;
        var ou = kind == Kind.Users ? state.Settings.UsersOU : state.Settings.ComputersOU;
        var filter = kind == Kind.Users ? LdapFilter.Users : LdapFilter.Computers;
        var depth = state.Settings.IncludeSubOUs ? SearchDepth.Subtree : SearchDepth.OneLevel;
        status.Text = S("Reading...");
        try
        {
            var objects = await Task.Run(() => state.Directory.Search(ou, filter, depth, Attributes, token), token);
            if (IsDisposed)
            {
                return;
            }

            grid.Rows.Clear();
            foreach (var o in objects.OrderBy(o => o.SamAccountName, StringComparer.OrdinalIgnoreCase))
            {
                var disabled = o.AccountControl.HasFlag(UserAccountControl.AccountDisable);
                var logon = AdTime.FromFileTime(o.Get("lastLogonTimestamp"));
                grid.Rows.Add(
                    o.SamAccountName ?? "",
                    o.Get("displayName") ?? o.Get("cn") ?? "",
                    disabled ? S("Disabled") : S("Enabled"),
                    logon?.ToLocalTime().ToString("yyyy-MM-dd", Culture) ?? "-",
                    (kind == Kind.Users ? o.Get("description") : o.Get("operatingSystem")) ?? "",
                    o.Dn);
            }

            status.Text = F("{0} objects", objects.Count);
        }
        catch (OperationCanceledException)
        {
        }
        catch (DirectoryException ex)
        {
            if (!IsDisposed)
            {
                status.Text = Messages.ForCode(ex.Code) + " " + ex.Detail;
            }
        }
    }
}
