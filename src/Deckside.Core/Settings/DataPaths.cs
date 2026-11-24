using System.Globalization;
using Deckside.Core.Time;

namespace Deckside.Core.Settings;

/// <summary>
/// Where Deckside keeps its files: next to the exe by default, or in <see cref="AppSettings.DataFolder"/>.
/// These folders contain personal data and are excluded from the repository.
/// </summary>
public sealed class DataPaths
{
    public DataPaths(string baseFolder) => Root = Path.GetFullPath(baseFolder);

    public static DataPaths From(string exeFolder, AppSettings settings) =>
        new(string.IsNullOrWhiteSpace(settings.DataFolder) ? exeFolder : Path.Combine(exeFolder, settings.DataFolder));

    public string Root { get; }

    public string SettingsFile => Path.Combine(Root, "Settings", "settings.json");

    public string Users => Path.Combine(Root, "Users");

    public string Computers => Path.Combine(Root, "Computers");

    public string Offboarding => Path.Combine(Root, "Offboarding");

    public string Reports => Path.Combine(Root, "Reports");

    public string Profiles => Path.Combine(Root, "Profiles");

    public string Logs => Path.Combine(Root, "Logs");

    public string ProfilesFile(AppSettings settings) => Path.Combine(Root, settings.ProfilesFile);

    /// <summary>Evidence folder of one operation, e.g. <c>USER_u00042_20261124-153000</c>.</summary>
    public static string EvidenceFolderName(EvidenceKind kind, string? key, IClock clock)
    {
        var stamp = clock.UtcNow.ToLocalTime().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var prefix = kind switch
        {
            EvidenceKind.User => "USER",
            EvidenceKind.Computer => "COMPUTER",
            EvidenceKind.Offboarding => "OFFBOARD",
            EvidenceKind.Profile => "PROFILE",
            EvidenceKind.Report => "REPORT",
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        var safeKey = SafeFilePart(key);
        return kind is EvidenceKind.Offboarding or EvidenceKind.Report
            ? string.Join('_', new[] { prefix, stamp, safeKey }.Where(p => p.Length > 0))
            : string.Join('_', new[] { prefix, safeKey, stamp }.Where(p => p.Length > 0));
    }

    /// <summary>Keeps letters, digits, <c>- _ .</c>; everything else becomes <c>-</c>.</summary>
    public static string SafeFilePart(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var chars = value.Trim().Select(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' ? c : '-').ToArray();
        var result = new string(chars).Trim('.', '-');
        return result.Length > 40 ? result[..40] : result;
    }
}

public enum EvidenceKind
{
    User,
    Computer,
    Offboarding,
    Profile,
    Report,
}
