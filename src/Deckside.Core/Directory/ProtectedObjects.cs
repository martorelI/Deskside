namespace Deckside.Core.Directory;

/// <summary>
/// Security rule 3: privileged groups and protected accounts are never written to. Identified by well-known
/// SID / RID, never by name (names are localized and can be renamed), plus <c>adminCount = 1</c> (objects
/// protected by AdminSDHolder). DnsAdmins has no fixed RID, so it is the only one also matched by name.
/// </summary>
public static class ProtectedObjects
{
    /// <summary>BUILTIN groups (S-1-5-32-RID).</summary>
    private static readonly HashSet<int> BuiltinRids =
    [
        544, // Administrators
        548, // Account Operators
        549, // Server Operators
        550, // Print Operators
        551, // Backup Operators
        552, // Replicator
    ];

    /// <summary>Domain groups (S-1-5-21-domain-RID).</summary>
    private static readonly HashSet<int> DomainGroupRids =
    [
        498, // Enterprise Read-only Domain Controllers
        512, // Domain Admins
        516, // Domain Controllers
        517, // Cert Publishers
        518, // Schema Admins
        519, // Enterprise Admins
        520, // Group Policy Creator Owners
        521, // Read-only Domain Controllers
        526, // Key Admins
        527, // Enterprise Key Admins
    ];

    /// <summary>Domain accounts that are never modified (Administrator, Guest, krbtgt).</summary>
    private static readonly HashSet<int> DomainAccountRids = [500, 501, 502];

    /// <summary>Groups without a fixed RID that are matched by sAMAccountName.</summary>
    private static readonly string[] ProtectedGroupNames = ["DnsAdmins"];

    public static bool IsProtectedGroupSid(string? sid)
    {
        if (!TrySplitSid(sid, out var prefix, out var rid))
        {
            return false;
        }

        return (prefix == "S-1-5-32" && BuiltinRids.Contains(rid)) ||
               (prefix.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) && DomainGroupRids.Contains(rid));
    }

    /// <summary>True when the group can never be written (membership added or removed).</summary>
    public static bool IsProtectedGroup(DirectoryObject group, IEnumerable<string>? extraProtectedNames = null)
    {
        if (group.HasAdminCount || IsProtectedGroupSid(group.Sid))
        {
            return true;
        }

        var sam = group.SamAccountName;
        if (sam is null)
        {
            return false;
        }

        return ProtectedGroupNames.Concat(extraProtectedNames ?? []).Contains(sam, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>True when the account can never be modified, moved or disabled.</summary>
    public static bool IsProtectedAccount(DirectoryObject account)
    {
        if (account.HasAdminCount)
        {
            return true;
        }

        return TrySplitSid(account.Sid, out var prefix, out var rid) &&
               prefix.StartsWith("S-1-5-21-", StringComparison.OrdinalIgnoreCase) &&
               DomainAccountRids.Contains(rid);
    }

    private static bool TrySplitSid(string? sid, out string prefix, out int rid)
    {
        prefix = string.Empty;
        rid = 0;
        if (string.IsNullOrEmpty(sid) || !sid.StartsWith("S-", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var dash = sid.LastIndexOf('-');
        if (dash <= 2 || !int.TryParse(sid.AsSpan(dash + 1), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out rid))
        {
            return false;
        }

        prefix = sid[..dash].ToUpperInvariant();
        return true;
    }
}
