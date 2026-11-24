namespace Deckside.App.Ui;

/// <summary>Messages and questions ("No" is the default answer).</summary>
internal static class Dialogs
{
    private static string Name => Deckside.Core.AppInfo.Name;

    public static void Info(IWin32Window? owner, string text, string? title = null) =>
        MessageBox.Show(owner, text, title ?? Name, MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static void Error(IWin32Window? owner, string text, string? title = null) =>
        MessageBox.Show(owner, text, title ?? Name, MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static bool Ask(IWin32Window? owner, string text, string? title = null) =>
        MessageBox.Show(owner, text, title ?? Name, MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
}
