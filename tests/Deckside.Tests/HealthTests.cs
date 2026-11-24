using System.Globalization;
using Deckside.Core.Directory;
using Deckside.Core.Health;
using Deckside.Core.Localization;
using Deckside.Core.Settings;
using Deckside.Core.Text;
using Deckside.Tests.Support;

namespace Deckside.Tests;

public class HealthRulesTests
{
    private static readonly ReportSettings Rules = new();

    private static string Ft(int daysAgo) => AdTime.ToFileTime(TestDomain.Now.AddDays(-daysAgo)).ToString(CultureInfo.InvariantCulture);

    private static string Gt(int daysAgo) => AdTime.ToGeneralizedTime(TestDomain.Now.AddDays(-daysAgo));

    private static TestDomain Sample()
    {
        var d = new TestDomain();
        var u = TestDomain.UsersOu;
        d.AddUser(u, "U00001", "Ok", "User", extra: [("lastLogonTimestamp", Ft(3)), ("pwdLastSet", Ft(10)), ("msDS-UserPasswordExpiryTimeComputed", Ft(-80)), ("whenCreated", Gt(400)), ("msDS-User-Account-Control-Computed", "0"), ("badPwdCount", "0")]);
        d.AddUser(u, "U00002", "Old", "Logon", extra: [("lastLogonTimestamp", Ft(120)), ("lastLogon", Ft(95)), ("pwdLastSet", Ft(50)), ("whenCreated", Gt(400))]);
        d.AddUser(u, "U00003", "Never", "Logged", extra: [("pwdLastSet", "0"), ("whenCreated", Gt(2))]);
        d.AddUser(u, "U00004", "Locked", "Out", extra: [("lastLogonTimestamp", Ft(1)), ("pwdLastSet", Ft(5)), ("msDS-User-Account-Control-Computed", "16"), ("lockoutTime", Ft(0)), ("badPwdCount", "5"), ("badPasswordTime", Ft(0)), ("whenCreated", Gt(30))]);
        d.AddUser(u, "U00005", "Expired", "Password", extra: [("lastLogonTimestamp", Ft(1)), ("pwdLastSet", Ft(100)), ("msDS-UserPasswordExpiryTimeComputed", Ft(10)), ("msDS-User-Account-Control-Computed", "8388608"), ("whenCreated", Gt(300))]);
        d.AddUser(u, "U00006", "Expiring", "Soon", uac: 66048, extra: [("lastLogonTimestamp", Ft(1)), ("pwdLastSet", Ft(80)), ("msDS-UserPasswordExpiryTimeComputed", Ft(-5)), ("accountExpires", Ft(-7)), ("whenCreated", Gt(300))]);
        d.AddUser(u, "U00007", "Gone", "Contract", uac: 514 | 32, extra: [("lastLogonTimestamp", Ft(200)), ("pwdLastSet", Ft(200)), ("accountExpires", Ft(30)), ("whenCreated", Gt(900))]);
        d.AddUser(u, "U00008", "Stale", "Lockout", extra: [("lastLogonTimestamp", Ft(1)), ("pwdLastSet", Ft(5)), ("lockoutTime", Ft(20)), ("whenCreated", Gt(30))]);
        d.AddComputer(TestDomain.ComputersOu, "PC00001", ("lastLogonTimestamp", Ft(2)), ("pwdLastSet", Ft(12)), ("operatingSystem", "Windows 11 Pro"), ("operatingSystemVersion", "10.0 (26100)"), ("whenCreated", Gt(500)));
        d.AddComputer(TestDomain.ComputersOu, "PC00002", ("lastLogonTimestamp", Ft(150)), ("pwdLastSet", Ft(150)), ("operatingSystem", "Windows 10 Pro"), ("whenCreated", Gt(900)));
        d.AddComputer(TestDomain.ComputersOu, "PC00003", ("pwdLastSet", Ft(1)), ("whenCreated", Gt(1)));
        return d;
    }

    private static HealthRow Row(string sam) => HealthRules.Evaluate(Sample().Get(sam), Rules, TestDomain.Clock);

    [Fact]
    public void Checks_per_account()
    {
        Assert.Empty(Row("U00001").Findings);
        Assert.Equal([HealthCodes.Inactive], Row("U00002").Findings);
        Assert.Equal(95, Row("U00002").DaysSinceLogon); // the most recent of lastLogon and lastLogonTimestamp
        Assert.Equal([HealthCodes.PasswordMustChange, HealthCodes.NeverLoggedOn], Row("U00003").Findings);
        Assert.Equal([HealthCodes.LockedOut, HealthCodes.BadPasswords], Row("U00004").Findings);
        Assert.Equal([HealthCodes.PasswordExpired], Row("U00005").Findings);
        Assert.Equal([HealthCodes.AccountExpiring, HealthCodes.PasswordExpiring, HealthCodes.PasswordNeverExpires], Row("U00006").Findings);
        Assert.Equal([HealthCodes.Disabled, HealthCodes.AccountExpired, HealthCodes.PasswordNotRequired, HealthCodes.Inactive], Row("U00007").Findings);
        // Without the constructed attribute, lockoutTime alone is trusted.
        Assert.Equal([HealthCodes.LockedOut], Row("U00008").Findings);
    }

    [Fact]
    public void Computers()
    {
        Assert.Empty(Row("PC00001$").Findings);
        Assert.Equal([HealthCodes.Inactive, HealthCodes.MachinePasswordOld], Row("PC00002$").Findings);
        Assert.Equal([HealthCodes.NeverLoggedOn], Row("PC00003$").Findings);
        Assert.Equal("Windows 11 Pro", Row("PC00001$").OperatingSystem);
    }

    [Fact]
    public void Report_snapshot_and_thresholds_from_settings()
    {
        var d = Sample();
        var report = HealthReport.Build(d.Objects, TestDomain.UsersOu, d.Objects, TestDomain.ComputersOu, Rules, TestDomain.Clock);
        Assert.Equal(8, report.Users.Rows.Count);
        Assert.Equal(3, report.Computers.Rows.Count);
        Assert.Equal(2, report.Users.Count(HealthCodes.LockedOut));
        Snapshot.Match("health.report.txt", PlanText.Render(report, new Translator("en")));

        var strict = HealthRules.Evaluate(d.Get("U00001"), Rules with { InactiveDays = 2 }, TestDomain.Clock);
        Assert.Equal([HealthCodes.Inactive], strict.Findings);
    }
}
