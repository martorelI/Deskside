using System.Runtime.Versioning;
using Deckside.App.Ui;

namespace Deckside.App;

[SupportedOSPlatform("windows")]
internal static class Program
{
    [STAThread]
    private static void Main()
    {
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        using var state = new AppState(AppContext.BaseDirectory);
        Text.Initialize(state.Settings.Language);

        // The session starts by asking for the server and the account; nothing is shown without a connection.
        using (var login = new LoginForm(state))
        {
            if (login.ShowDialog() != DialogResult.OK)
            {
                return;
            }
        }

        Application.Run(new MainForm(state));
    }
}
