using System.Runtime.Versioning;
using System.Text;
using Deckside.Core.Settings;
using Deckside.Directory;

namespace Deckside.App;

/// <summary>The session: settings, where they live, and the connection (in memory only, never saved).</summary>
[SupportedOSPlatform("windows")]
internal sealed class AppState : IDisposable
{
    private IDirectory? connection;
    private GuardedDirectory? guarded;

    public AppState(string exeFolder)
    {
        ExeFolder = exeFolder;
        var bootstrap = new DataPaths(exeFolder);
        var result = SettingsLoader.Load(bootstrap.SettingsFile);
        Settings = result.Settings;
        Warnings = result.Warnings;
        Paths = DataPaths.From(exeFolder, Settings);
    }

    public string ExeFolder { get; }

    public AppSettings Settings { get; private set; }

    public IReadOnlyList<SettingsWarning> Warnings { get; private set; }

    public DataPaths Paths { get; private set; }

    /// <summary>Settings always live next to the exe, so the data folder can be changed from them.</summary>
    public string SettingsFile => new DataPaths(ExeFolder).SettingsFile;

    public bool IsConnected => connection is not null;

    /// <summary>The only way the pages reach the directory: every write is checked against the master OU.</summary>
    public GuardedDirectory Directory => guarded ?? throw new InvalidOperationException("Not connected.");

    public void Connected(IDirectory directory)
    {
        connection?.Dispose();
        connection = directory;
        guarded = new GuardedDirectory(directory, Settings.MasterOU);
    }

    /// <summary>Validates, saves and applies new settings. Returns the warnings (invalid values were replaced).</summary>
    public IReadOnlyList<SettingsWarning> Save(AppSettings settings)
    {
        var json = SettingsWriter.ToJson(settings);
        var result = SettingsLoader.Parse(json);
        System.IO.Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
        File.WriteAllText(SettingsFile, SettingsWriter.ToJson(result.Settings), new UTF8Encoding(false));
        Settings = result.Settings;
        Warnings = result.Warnings;
        Paths = DataPaths.From(ExeFolder, Settings);
        if (connection is not null)
        {
            // A new master OU takes effect at once: the gate is rebuilt with it.
            guarded = new GuardedDirectory(connection, Settings.MasterOU);
        }

        return result.Warnings;
    }

    public void Dispose() => connection?.Dispose();
}
