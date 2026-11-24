using System.Runtime.Versioning;
using Deckside.App.Ui;
using Deckside.Core;
using static Deckside.App.Ui.Text;

namespace Deckside.App.Pages;

[SupportedOSPlatform("windows")]
internal sealed class AboutPage : Page
{
    public AboutPage(MainForm shell)
        : base(shell, S("About"), AppInfo.Name + " " + AppInfo.Version)
    {
        var list = new CardList();

        var brand = new Panel { Height = 72, Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 6) };
        var logo = new PictureBox { Image = Theme.Logo, SizeMode = PictureBoxSizeMode.Zoom, Size = new Size(64, 64), Dock = DockStyle.Left };
        var names = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 6, 0, 0) };
        var name = Make.Label(AppInfo.Name, Theme.Title);
        name.Dock = DockStyle.Top;
        var version = Make.Label(F("Version {0}  ·  {1}", AppInfo.Version, AppInfo.Author), Theme.Body, Theme.Muted);
        version.Dock = DockStyle.Top;
        names.Controls.Add(version);
        names.Controls.Add(name);
        brand.Controls.Add(names);
        brand.Controls.Add(logo);
        var app = list.Add(new Card(S("Application")));
        brand.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
        app.Add(brand);
        app.Add(Make.Note(S("Manages users and computers of Active Directory only inside a master OU. Every change is previewed, confirmed and recorded; nothing is deleted.")));

        var data = list.Add(new Card(S("Personal data")));
        data.Add(Make.Note(S("The data folders (records, reports, exports) contain personal data. Protecting them is the responsibility of the operator. Passwords (initial and LAPS) are never written to disk.")));

        var marks = list.Add(new Card(S("Trademarks")));
        marks.Add(Make.Note(S("Microsoft, Windows and Active Directory are trademarks of Microsoft Corporation. Deckside is independent and is not affiliated with, sponsored or endorsed by Microsoft; the names are used only to indicate compatibility.")));
        SetBody(list);
    }
}
