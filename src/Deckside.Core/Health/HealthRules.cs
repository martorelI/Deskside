using Deckside.Core.Directory;
using Deckside.Core.Settings;
using Deckside.Core.Time;

namespace Deckside.Core.Health;

/// <summary>Codes of the checks of the health report (English; the UI translates them).</summary>
public static class HealthCodes
{
    public const string Disabled = "DISABLED";
    public const string AccountExpired = "ACCOUNT_EXPIRED";
    public const string AccountExpiring = "ACCOUNT_EXPIRING";
    public const string PasswordExpired = "PASSWORD_EXPIRED";
    public const string PasswordExpiring = "PASSWORD_EXPIRING";
    public const string PasswordMustChange = "PASSWORD_MUST_CHANGE";
    public const string PasswordNeverExpires = "PASSWORD_NEVER_EXPIRES";
    public const string PasswordNotRequired = "PASSWORD_NOT_REQUIRED";
    public const string Inactive = "INACTIVE";
    public const string NeverLoggedOn = "NEVER_LOGGED_ON";
    public const string LockedOut = "LOCKED_OUT";
    public const string BadPasswords = "BAD_PASSWORDS";
    public const string MachinePasswordOld = "MACHINE_PASSWORD_OLD";

    public static readonly IReadOnlyList<string> ForUsers =
    [
        Disabled, AccountExpired, AccountExpiring, PasswordExpired, PasswordExpiring, PasswordMustChange,
        PasswordNeverExpires, PasswordNotRequired, Inactive, NeverLoggedOn, LockedOut, BadPasswords,
    ];

    public static readonly IReadOnlyList<string> ForComputers = [Disabled, Inactive, NeverLoggedOn, MachinePasswordOld];
}

public enum HealthObjectKind
{
    User,
    Computer,
}

/// <summary>One line of the health report: the values read and the checks that failed.</summary>
public sealed record HealthRow
{
    public required string Dn { get; init; }

    public required HealthObjectKind Kind { get; init; }

    public string SamAccountName { get; init; } = "";

    public string Name { get; init; } = "";

    public bool Enabled { get; init; }

    public DateTimeOffset? AccountExpires { get; init; }

    public DateTimeOffset? PasswordLastSet { get; init; }

    public DateTimeOffset? PasswordExpires { get; init; }

    /// <summary>Most recent of <c>lastLogonTimestamp</c> (replicated, up to ~14 days late) and <c>lastLogon</c> (per DC).</summary>
    public DateTimeOffset? LastLogon { get; init; }

    public bool LockedOut { get; init; }

    public int? BadPasswordCount { get; init; }

    public DateTimeOffset? BadPasswordTime { get; init; }

    public DateTimeOffset? WhenCreated { get; init; }

    public DateTimeOffset? WhenChanged { get; init; }

    public string OperatingSystem { get; init; } = "";

    public string OperatingSystemVersion { get; init; } = "";

    public int? PasswordAgeDays { get; init; }

    public int? DaysSinceLogon { get; init; }

    public IReadOnlyList<string> Findings { get; init; } = [];

    public bool HasProblems => Findings.Count > 0;
}

/// <summary>
/// Rules of the health report from raw attribute values. Read only. The Operations layer reads the objects
/// with the attributes below; when <c>lastLogon</c> / <c>badPwdCount</c> come from several DCs it passes the
/// most recent / the highest value in the same attributes.
/// </summary>
public static class HealthRules
{
    public static readonly IReadOnlyList<string> Attributes =
    [
        "objectClass", "sAMAccountName", "displayName", "cn", "userAccountControl", "msDS-User-Account-Control-Computed",
        "accountExpires", "pwdLastSet", "msDS-UserPasswordExpiryTimeComputed", "lastLogonTimestamp", "lastLogon",
        "lockoutTime", "badPwdCount", "badPasswordTime", "whenCreated", "whenChanged", "operatingSystem",
        "operatingSystemVersion", "adminCount",
    ];

    public static HealthRow Evaluate(DirectoryObject obj, ReportSettings rules, IClock clock)
    {
        var now = clock.UtcNow;
        var kind = obj.IsComputer ? HealthObjectKind.Computer : HealthObjectKind.User;
        var uac = obj.AccountControl;
        var computed = (UserAccountControl)(obj.GetInt32("msDS-User-Account-Control-Computed") ?? 0);
        var hasComputed = obj.Get("msDS-User-Account-Control-Computed") is not null;
        var findings = new List<string>();

        var enabled = !uac.HasFlag(UserAccountControl.AccountDisable);
        if (!enabled)
        {
            findings.Add(HealthCodes.Disabled);
        }

        var pwdLastSetRaw = obj.GetInt64("pwdLastSet");
        var pwdLastSet = AdTime.FromFileTime(pwdLastSetRaw);
        int? passwordAge = pwdLastSet is null ? null : Days(now - pwdLastSet.Value);

        var lastLogon = Max(AdTime.FromFileTime(obj.Get("lastLogonTimestamp")), AdTime.FromFileTime(obj.Get("lastLogon")));
        int? sinceLogon = lastLogon is null ? null : Days(now - lastLogon.Value);
        var created = AdTime.FromGeneralizedTime(obj.Get("whenCreated"));
        if (lastLogon is null)
        {
            findings.Add(HealthCodes.NeverLoggedOn);
        }

        var reference = lastLogon ?? created;
        if (reference is not null && Days(now - reference.Value) >= rules.InactiveDays)
        {
            findings.Add(HealthCodes.Inactive);
        }

        DateTimeOffset? accountExpires = null;
        DateTimeOffset? passwordExpires = null;
        var locked = false;
        int? badCount = null;
        if (kind == HealthObjectKind.User)
        {
            accountExpires = AdTime.FromFileTime(obj.Get("accountExpires"));
            if (accountExpires is not null)
            {
                if (accountExpires <= now)
                {
                    findings.Add(HealthCodes.AccountExpired);
                }
                else if (accountExpires <= now.AddDays(rules.ExpiryWarningDays))
                {
                    findings.Add(HealthCodes.AccountExpiring);
                }
            }

            var neverExpires = uac.HasFlag(UserAccountControl.DontExpirePassword);
            if (neverExpires)
            {
                findings.Add(HealthCodes.PasswordNeverExpires);
            }

            if (uac.HasFlag(UserAccountControl.PasswordNotRequired))
            {
                findings.Add(HealthCodes.PasswordNotRequired);
            }

            if (pwdLastSetRaw == 0)
            {
                findings.Add(HealthCodes.PasswordMustChange);
            }
            else
            {
                passwordExpires = AdTime.FromFileTime(obj.Get("msDS-UserPasswordExpiryTimeComputed"));
                if (computed.HasFlag(UserAccountControl.PasswordExpired) || (passwordExpires is not null && passwordExpires <= now))
                {
                    findings.Add(HealthCodes.PasswordExpired);
                }
                else if (passwordExpires is not null && passwordExpires <= now.AddDays(rules.ExpiryWarningDays))
                {
                    findings.Add(HealthCodes.PasswordExpiring);
                }
            }

            // The constructed flag knows the lockout duration; lockoutTime alone stays set until the next logon.
            locked = hasComputed ? computed.HasFlag(UserAccountControl.Lockout) : (obj.GetInt64("lockoutTime") ?? 0) > 0;
            if (locked)
            {
                findings.Add(HealthCodes.LockedOut);
            }

            badCount = obj.GetInt32("badPwdCount");
            if (badCount >= rules.BadPasswordThreshold)
            {
                findings.Add(HealthCodes.BadPasswords);
            }
        }
        else if (passwordAge is not null && passwordAge >= rules.MachinePasswordMaxDays)
        {
            findings.Add(HealthCodes.MachinePasswordOld);
        }

        var order = kind == HealthObjectKind.User ? HealthCodes.ForUsers : HealthCodes.ForComputers;
        return new HealthRow
        {
            Dn = obj.Dn,
            Kind = kind,
            SamAccountName = obj.SamAccountName ?? "",
            Name = obj.Get("displayName") ?? obj.Get("cn") ?? DistinguishedName.FirstRdn(obj.Dn).Value,
            Enabled = enabled,
            AccountExpires = accountExpires,
            PasswordLastSet = pwdLastSet,
            PasswordExpires = passwordExpires,
            LastLogon = lastLogon,
            LockedOut = locked,
            BadPasswordCount = badCount,
            BadPasswordTime = kind == HealthObjectKind.User ? AdTime.FromFileTime(obj.Get("badPasswordTime")) : null,
            WhenCreated = created,
            WhenChanged = AdTime.FromGeneralizedTime(obj.Get("whenChanged")),
            OperatingSystem = obj.Get("operatingSystem") ?? "",
            OperatingSystemVersion = obj.Get("operatingSystemVersion") ?? "",
            PasswordAgeDays = passwordAge,
            DaysSinceLogon = sinceLogon,
            Findings = [.. order.Where(findings.Contains)],
        };
    }

    private static int Days(TimeSpan span) => (int)Math.Floor(span.TotalDays);

    private static DateTimeOffset? Max(DateTimeOffset? a, DateTimeOffset? b) =>
        a is null ? b : b is null ? a : (a > b ? a : b);
}

/// <summary>The rows of one OU and the count of each check.</summary>
public sealed record HealthSection(HealthObjectKind Kind, string Ou, IReadOnlyList<HealthRow> Rows)
{
    public int Count(string code) => Rows.Count(r => r.Findings.Contains(code));

    public int WithProblems => Rows.Count(r => r.HasProblems);

    public IReadOnlyList<string> Codes => Kind == HealthObjectKind.User ? HealthCodes.ForUsers : HealthCodes.ForComputers;
}

public sealed record HealthReport(DateTimeOffset GeneratedUtc, HealthSection Users, HealthSection Computers)
{
    public static HealthReport Build(IEnumerable<DirectoryObject> users, string usersOu, IEnumerable<DirectoryObject> computers, string computersOu, ReportSettings rules, IClock clock)
    {
        static IReadOnlyList<HealthRow> Rows(IEnumerable<DirectoryObject> objects, ReportSettings rules, IClock clock) =>
            [.. objects.Select(o => HealthRules.Evaluate(o, rules, clock)).OrderBy(r => r.SamAccountName, StringComparer.OrdinalIgnoreCase)];

        return new HealthReport(
            clock.UtcNow,
            new HealthSection(HealthObjectKind.User, usersOu, Rows(users.Where(u => u.IsUser), rules, clock)),
            new HealthSection(HealthObjectKind.Computer, computersOu, Rows(computers.Where(c => c.IsComputer), rules, clock)));
    }
}
