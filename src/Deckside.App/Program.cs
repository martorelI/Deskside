using System.Globalization;
using System.Runtime.Versioning;
using Deckside.App.Ui;

namespace Deckside.App;

[SupportedOSPlatform("windows")]
internal static class Program
{
    private static string BaseFolder => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Crash(e.ExceptionObject as Exception);
        try
        {
            ApplicationConfiguration.Initialize();
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => Report(e.Exception);

            using var state = new AppState(BaseFolder);
            Text.Initialize(state.Settings.Language);

            // The session starts by asking for the server and the account; nothing is shown without a connection.
            using (var login = new LoginForm(state))
            {
                if (login.ShowDialog() != DialogResult.OK)
                {
                    return;
                }
            }

            using var form = new MainForm(state);
            Application.Run(form);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            Crash(ex);
        }
    }

    /// <summary>An error on the window thread: logged and shown; the application keeps running.</summary>
    private static void Report(Exception ex)
    {
        var file = WriteError(ex);
        Dialogs.Error(null, ex.Message + (file is null ? string.Empty : "\r\n\r\n" + file));
    }

    /// <summary>An error that ends the application: never silently. The detail goes to deckside-error.log.</summary>
    private static void Crash(Exception? ex)
    {
        var file = WriteError(ex);
        try
        {
            MessageBox.Show((ex?.Message ?? "?") + "\r\n\r\n" + (file ?? string.Empty), Deckside.Core.AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        catch (InvalidOperationException)
        {
        }

        Environment.Exit(1);
    }

    /// <summary>Appends the exception (with its stack) to deckside-error.log next to the program, or in TEMP. Never contains passwords.</summary>
    private static string? WriteError(Exception? ex)
    {
        var text = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " " + Environment.OSVersion + " .NET " + Environment.Version + "\r\n" + ex + "\r\n\r\n";
        foreach (var dir in new[] { BaseFolder, Path.GetTempPath() })
        {
            try
            {
                var path = Path.Combine(dir, "deckside-error.log");
                File.AppendAllText(path, text);
                return path;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
            }
        }

        return null;
    }
}
