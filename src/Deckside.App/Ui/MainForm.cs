using System.ComponentModel;
using System.Runtime.Versioning;
using Deckside.App.Pages;
using Deckside.Directory;
using static Deckside.App.Ui.Text;

namespace Deckside.App.Ui;

/// <summary>The shell: navigation on the left, the page in the middle, status and connection at the bottom.</summary>
[SupportedOSPlatform("windows")]
internal sealed class MainForm : Form
{
    private readonly Panel host = new() { Dock = DockStyle.Fill, BackColor = Theme.Background };
    private readonly FlowLayoutPanel nav = new() { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Sidebar, Padding = new Padding(0, 8, 0, 0) };
    private readonly Label status = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, ForeColor = Theme.Muted, Font = Theme.Small, AutoEllipsis = true, Padding = new Padding(16, 0, 0, 0) };
    private readonly Label connection = new() { Dock = DockStyle.Right, AutoSize = true, TextAlign = ContentAlignment.MiddleRight, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(0, 8, 16, 0) };
    private readonly Banner blocked = new();
    private readonly Button stop;
    private readonly List<(Button Button, Page Page)> pages = [];
    private CancellationTokenSource? cts;
    private Page? current;

    public MainForm(AppState app)
    {
        // Sizes in this code are for 96 DPI; WinForms scales them (and the fonts) to the screen.
        AutoScaleDimensions = new SizeF(96F, 96F);
        AutoScaleMode = AutoScaleMode.Dpi;
        SuspendLayout();
        App = app;
        Text = Deckside.Core.AppInfo.Name;
        Font = Theme.Body;
        BackColor = Theme.Background;
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(1080, 700);
        Size = new Size(1280, 820);
        try
        {
            Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        }
        catch (ArgumentException)
        {
        }

        stop = Make.Danger(S("Stop"), (_, _) => cts?.Cancel());
        stop.Dock = DockStyle.Right;
        stop.Visible = false;

        var sidebar = new Panel { Dock = DockStyle.Left, Width = 236, BackColor = Theme.Sidebar };
        var brand = new Panel { Dock = DockStyle.Top, Height = 92, BackColor = Theme.Sidebar, Padding = new Padding(18, 20, 12, 12) };
        var logo = new PictureBox { Image = Theme.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(44, 44), Dock = DockStyle.Left };
        var name = new Label { Text = Deckside.Core.AppInfo.Name, ForeColor = Color.White, Font = Theme.Brand, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft, Padding = new Padding(10, 0, 0, 0) };
        brand.Controls.Add(name);
        brand.Controls.Add(logo);
        var version = new Label { Text = F("Version {0}", Deckside.Core.AppInfo.Version), Dock = DockStyle.Bottom, Height = 34, ForeColor = Theme.Muted, Font = Theme.Small, Padding = new Padding(20, 0, 0, 0) };
        sidebar.Controls.Add(nav);
        sidebar.Controls.Add(version);
        sidebar.Controls.Add(brand);

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 44, BackColor = Theme.Surface, Padding = new Padding(0, 6, 12, 6) };
        bar.Controls.Add(status);
        bar.Controls.Add(connection);
        bar.Controls.Add(stop);
        var barLine = new Panel { Dock = DockStyle.Bottom, Height = 1, BackColor = Theme.Border };

        Controls.Add(host);
        Controls.Add(blocked);
        Controls.Add(barLine);
        Controls.Add(bar);
        Controls.Add(sidebar);

        AddPage("☺", S("Users"), new ObjectListPage(this, ObjectListPage.Kind.Users));
        AddPage("▣", S("Computers"), new ObjectListPage(this, ObjectListPage.Kind.Computers));
        AddPage("⚿", "LAPS", new LapsPage(this));
        AddPage("⇥", S("Offboarding"), new PlannedPage(this, S("Offboarding"), S("Find an employee, check whether the account is still active, then disable it and move it to the disabled OU.")));
        AddPage("✚", S("Health report"), new PlannedPage(this, S("Health report"), S("Expired, inactive, locked-out objects and password problems of the users and computers OUs, with Excel export.")));
        AddPage("☷", S("Profiles"), new PlannedPage(this, S("Profiles"), S("Group templates to assign or change on users and computers, only with groups inside the master OU.")));
        AddPage("⚙", S("Settings"), new SettingsPage(this));
        AddPage("ⓘ", S("About"), new AboutPage(this));
        RefreshConnection();
        // Without a usable master OU the operator lands on Settings.
        Show(App.Directory.WriteBlockedReason() is null ? 0 : 6);
        SetStatus(S("Ready."));
        ResumeLayout(true);
        FormClosing += (_, e) =>
        {
            if (Busy && !Dialogs.Ask(this, S("A query is running. Close anyway?")))
            {
                e.Cancel = true;
            }
        };
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public AppState App { get; }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Busy { get; private set; }

    private void AddPage(string glyph, string title, Page page)
    {
        var index = pages.Count;
        var b = new Button
        {
            Text = "   " + glyph + "   " + title,
            TextAlign = ContentAlignment.MiddleLeft,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Theme.SidebarText,
            BackColor = Theme.Sidebar,
            Font = Theme.Nav,
            Width = 236,
            Height = 46,
            Margin = new Padding(0),
            Cursor = Cursors.Hand,
            UseVisualStyleBackColor = false,
        };
        b.FlatAppearance.BorderSize = 0;
        b.FlatAppearance.MouseOverBackColor = Theme.SidebarHover;
        b.Click += (_, _) => Show(index);
        b.Paint += (_, e) =>
        {
            if (current == page)
            {
                using var brush = new SolidBrush(Theme.Sky);
                e.Graphics.FillRectangle(brush, 0, 8, 4, b.Height - 16);
            }
        };
        nav.Controls.Add(b);
        page.Visible = false;
        host.Controls.Add(page);
        pages.Add((b, page));
    }

    private void Show(int index)
    {
        if (Busy)
        {
            return;
        }

        if (current is not null && current != pages[index].Page)
        {
            current.Deactivated();
        }

        for (var i = 0; i < pages.Count; i++)
        {
            var on = i == index;
            pages[i].Page.Visible = on;
            pages[i].Button.BackColor = on ? Theme.SidebarHover : Theme.Sidebar;
            pages[i].Button.ForeColor = on ? Color.White : Theme.SidebarText;
            if (on)
            {
                current = pages[i].Page;
            }

            pages[i].Button.Invalidate();
        }

        current?.Activated();
    }

    public void SetStatus(string text) => status.Text = text;

    /// <summary>Connection summary at the bottom right, and the red band when nothing can be written.</summary>
    public void RefreshConnection()
    {
        var info = App.Directory.Info;
        var dc = info.DcHostName.Length > 0 && !string.Equals(info.DcHostName, info.Server, StringComparison.OrdinalIgnoreCase) ? " (" + info.DcHostName + ")" : "";
        var who = info.BoundAs.Length > 0 ? info.BoundAs : S("my Windows session");
        connection.Text = "● " + info.Server + dc + "   ·   " + who + "   ·   " + Messages.Encryption(info.Encryption);
        connection.ForeColor = Theme.Success;
        var reason = App.Directory.WriteBlockedReason();
        blocked.Show(reason is null ? null : "⚠  " + Messages.ForCode(reason), Theme.Danger, Theme.DangerSoft);
    }

    /// <summary>
    /// Runs a directory query on a background thread with the Stop button active. Directory errors are shown
    /// in the status bar and in a dialog. Returns default if it failed or was stopped.
    /// </summary>
    public async Task<T?> RunAsync<T>(string working, Func<CancellationToken, T> work)
    {
        if (Busy)
        {
            return default;
        }

        Busy = true;
        cts = new CancellationTokenSource();
        foreach (var (b, _) in pages)
        {
            b.Enabled = false;
        }

        current?.SetBusy(true);
        stop.Visible = true;
        UseWaitCursor = true;
        SetStatus(working);
        try
        {
            var token = cts.Token;
            return await Task.Run(() => work(token), token).ConfigureAwait(true);
        }
        catch (OperationCanceledException)
        {
            SetStatus(S("Stopped by the operator."));
            return default;
        }
        catch (DirectoryException ex)
        {
            var text = Messages.ForCode(ex.Code) + (ex.Detail.Length > 0 ? Environment.NewLine + ex.Detail : "");
            SetStatus(Messages.ForCode(ex.Code));
            Dialogs.Error(this, text);
            return default;
        }
        finally
        {
            UseWaitCursor = false;
            stop.Visible = false;
            foreach (var (b, _) in pages)
            {
                b.Enabled = true;
            }

            Busy = false;
            current?.SetBusy(false);
            cts.Dispose();
            cts = null;
        }
    }
}
