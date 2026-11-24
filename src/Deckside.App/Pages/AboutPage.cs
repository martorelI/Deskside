using Deckside.App.Ui;
using static Deckside.App.Ui.Text;
using Deckside.Core;

namespace Deckside.App.Pages;

internal sealed class AboutPage : FlowLayoutPanel
{
    public AboutPage()
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        Controls.Add(Theme.Heading(AppInfo.Name + " " + AppInfo.Version));
        Controls.Add(Theme.Note(F("Author: {0}", AppInfo.Author)));
        Controls.Add(Theme.Note(S("Manages users and computers of Active Directory only inside a master OU. Every change is previewed, confirmed and recorded; nothing is deleted.")));
        Controls.Add(Theme.Note(S("The data folders (records, reports, exports) contain personal data. Protecting them is the responsibility of the operator.")));
        Controls.Add(Theme.Note(S("Microsoft, Windows and Active Directory are trademarks of Microsoft Corporation. Deckside is independent and is not affiliated with, sponsored or endorsed by Microsoft; the names are used only to indicate compatibility.")));
    }
}
