using System.Net;

namespace Deckside.Directory;

/// <summary>
/// How to reach the domain. The password lives only in memory for the session (security rule 5): it is
/// never written to settings, logs or records.
/// </summary>
public sealed class ConnectionOptions
{
    /// <summary>IP address (preferred) or host name of a domain controller.</summary>
    public required string Server { get; init; }

    /// <summary>0 = 389, or 636 with LDAPS.</summary>
    public int Port { get; init; }

    public bool UseLdaps { get; init; }

    /// <summary>true = the Windows session of the operator; false = <see cref="UserName"/> / <see cref="Password"/>.</summary>
    public bool UseCurrentWindowsSession { get; init; }

    /// <summary><c>DOMAIN\user</c> or <c>user@domain</c>.</summary>
    public string UserName { get; init; } = "";

    public string Password { get; init; } = "";

    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);

    internal NetworkCredential? ToCredential()
    {
        if (UseCurrentWindowsSession)
        {
            return null;
        }

        var user = UserName.Trim();
        var slash = user.IndexOf('\\', StringComparison.Ordinal);
        return slash > 0
            ? new NetworkCredential(user[(slash + 1)..], Password, user[..slash])
            : new NetworkCredential(user, Password);
    }
}
