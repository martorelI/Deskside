using Deckside.App.Ui;
using static Deckside.App.Ui.Text;

namespace Deckside.App.Pages;

/// <summary>A section that is designed but not built yet.</summary>
internal sealed class PlannedPage : FlowLayoutPanel
{
    public PlannedPage(string title, string description)
    {
        FlowDirection = FlowDirection.TopDown;
        WrapContents = false;
        Controls.Add(Theme.Heading(title));
        Controls.Add(Theme.Note(description));
        Controls.Add(Theme.Note(S("Coming in a next phase.")));
    }
}
