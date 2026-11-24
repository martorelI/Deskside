using System.Runtime.Versioning;
using Deckside.App.Ui;
using static Deckside.App.Ui.Text;

namespace Deckside.App.Pages;

/// <summary>A section that is designed but not built yet.</summary>
[SupportedOSPlatform("windows")]
internal sealed class PlannedPage : Page
{
    public PlannedPage(MainForm shell, string title, string description)
        : base(shell, title, description)
    {
        var list = new CardList();
        var card = list.Add(new Card(S("Coming in a next phase"), description));
        card.Add(Make.Note(S("Every change will be previewed, confirmed and recorded, and only inside the master OU.")));
        SetBody(list);
    }
}
