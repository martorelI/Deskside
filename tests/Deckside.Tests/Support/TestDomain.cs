using System.Globalization;
using Deckside.Core.Directory;
using Deckside.Core.Localization;
using Deckside.Core.Naming;
using Deckside.Core.Plans;
using Deckside.Core.Profiles;
using Deckside.Core.Settings;
using Deckside.Core.Time;

namespace Deckside.Tests.Support;

/// <summary>
/// Synthetic directory for the Core tests: domain example.local, invented names. It is a list of
/// <see cref="DirectoryObject"/> plus a uniqueness checker over it. (The full FakeDirectory comes with phase 2.)
/// </summary>
internal sealed class TestDomain : IUniquenessChecker
{
    public const string Domain = "example.local";
    public const string Base = "DC=example,DC=local";
    public const string DomainSid = "S-1-5-21-1000-2000-3000";
    public const string Root = "OU=Deckside Test," + Base;
    public const string UsersOu = "OU=Staff," + Root;
    public const string ComputersOu = "OU=Workstations," + Root;
    public const string DisabledOu = "OU=Disabled," + Root;
    public const string GroupsOu = "OU=Groups," + Root;

    public static readonly DateTimeOffset Now = new(2026, 11, 24, 15, 0, 0, TimeSpan.Zero);

    public static readonly IClock Clock = new FixedClock(Now);

    private int nextRid = 1100;

    public List<DirectoryObject> Objects { get; } = [];

    public static string Group(string name) => "CN=" + name + "," + GroupsOu;

    public static AppSettings Settings => new()
    {
        Language = "en",
        MasterOU = Root,
        UsersOU = UsersOu,
        ComputersOU = ComputersOu,
        DisabledOU = DisabledOu,
        DefaultComputerGroups = [Group("GG-Printers")],
    };

    /// <summary>A small domain with users, computers, groups and the protected cases.</summary>
    public static TestDomain Standard()
    {
        var d = new TestDomain();
        d.AddGroup("CN=Domain Admins,CN=Users," + Base, "Domain Admins", rid: 512, adminCount: true);
        d.AddGroup("CN=Domain Users,CN=Users," + Base, "Domain Users", rid: 513);
        d.AddGroup("CN=Administrators,CN=Builtin," + Base, "Administrators", sid: "S-1-5-32-544", adminCount: true);
        d.AddGroup("CN=DnsAdmins,CN=Users," + Base, "DnsAdmins");
        foreach (var g in new[] { "GG-Accounting", "GG-Warehouse", "GG-VPN", "GG-Printers", "GG-Kiosk", "GG-Projects" })
        {
            d.AddGroup(Group(g), g);
        }

        d.AddGroup(Group("GG-Tier0"), "GG-Tier0", adminCount: true);
        d.AddGroup(Group("Backup Ops Copy"), "Backup Ops Copy", sid: "S-1-5-32-551"); // well-known SID, no adminCount yet

        d.AddUser(UsersOu, "U00001", "Ana", "Lopez", groups: [Group("GG-Accounting"), Group("GG-VPN")]);
        d.AddUser(UsersOu, "U00007", "Bruno", "Diaz");
        d.AddUser("OU=Sales," + UsersOu, "U00041", "Carla", "Mendez", groups: [Group("GG-Warehouse"), Group("GG-Projects")]);
        d.AddUser(UsersOu, "U0099", "Old", "Format"); // 4 digits: not part of the 5-digit sequence
        d.AddUser(UsersOu, "svc-backup", "Backup", "Service");
        d.AddUser(UsersOu, "U00030", "Admin", "Person", adminCount: true, groups: ["CN=Domain Admins,CN=Users," + Base]);
        d.AddUser("OU=Other," + Base, "U00050", "Outside", "Scope"); // the highest number, outside the OUs
        d.Objects.Add(Obj("CN=juan.perez,CN=Contacts," + Base, ("objectClass", ["top", "contact"]), ("cn", ["juan.perez"]),
            ("mail", ["juan.perez@example.local"])));
        d.AddUser(UsersOu, "U00012", "Maria", "Ruiz", mail: "maria.ruiz@example.local", proxy: ["SMTP:maria.ruiz@example.local", "smtp:mruiz@example.local"]);

        d.AddComputer(ComputersOu, "PC00003");
        d.AddComputer(ComputersOu, "PC00009");
        d.AddComputer("OU=Servers," + Base, "PC00020");
        return d;
    }

    public PlanningContext Context(AppSettings? settings = null, ProfileCatalog? profiles = null, string language = "en") =>
        new(settings ?? Settings, new Translator(language), this, profiles ?? Profiles(), FindGroup, Clock);

    public static ProfileCatalog Profiles() => new(
    [
        new GroupProfile("Accounting", ProfileTarget.User, [Group("GG-Accounting"), Group("GG-VPN")]),
        new GroupProfile("Warehouse", ProfileTarget.User, [Group("GG-Warehouse")]),
        new GroupProfile("Kiosk PC", ProfileTarget.Computer, [Group("GG-Kiosk"), Group("GG-Printers")]),
    ]);

    public DirectoryObject? FindGroup(string dn) => Objects.FirstOrDefault(o => o.IsGroup && DistinguishedName.AreEqual(o.Dn, dn));

    public DirectoryObject Get(string sam) => Objects.First(o => string.Equals(o.SamAccountName, sam, StringComparison.OrdinalIgnoreCase));

    public IReadOnlyList<DirectoryObject> GroupsOf(DirectoryObject o) =>
        [.. o.GetAll("memberOf").Select(FindGroup).Where(g => g is not null).Select(g => g!)];

    public IEnumerable<ExistingName> SamNames(string prefix) =>
        Objects.Where(o => o.SamAccountName?.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) == true)
            .Select(o => new ExistingName(o.SamAccountName!, o.Dn));

    public DirectoryObject AddUser(string ou, string sam, string given, string sn, bool adminCount = false, IReadOnlyList<string>? groups = null,
        string? mail = null, IReadOnlyList<string>? proxy = null, int uac = 512, params (string Name, string Value)[] extra)
    {
        var cn = given + " " + sn;
        var attributes = new List<(string, IReadOnlyList<string>)>
        {
            ("objectClass", ["top", "person", "organizationalPerson", "user"]),
            ("cn", [cn]), ("sAMAccountName", [sam]), ("givenName", [given]), ("sn", [sn]), ("displayName", [cn]),
            ("userPrincipalName", [sam.ToLowerInvariant() + "@" + Domain]),
            ("objectSid", [DomainSid + "-" + (nextRid++).ToString(CultureInfo.InvariantCulture)]),
            ("userAccountControl", [uac.ToString(CultureInfo.InvariantCulture)]),
            ("primaryGroupID", ["513"]),
            ("memberOf", groups ?? []),
        };
        if (adminCount)
        {
            attributes.Add(("adminCount", ["1"]));
        }

        if (mail is not null)
        {
            attributes.Add(("mail", [mail]));
        }

        if (proxy is not null)
        {
            attributes.Add(("proxyAddresses", proxy));
        }

        attributes.AddRange(extra.Select(e => (e.Name, (IReadOnlyList<string>)[e.Value])));
        var o = Obj("CN=" + DistinguishedName.EscapeValue(cn) + "," + ou, [.. attributes]);
        Objects.Add(o);
        return o;
    }

    public DirectoryObject AddComputer(string ou, string name, params (string Name, string Value)[] extra)
    {
        var attributes = new List<(string, IReadOnlyList<string>)>
        {
            ("objectClass", ["top", "person", "organizationalPerson", "user", "computer"]),
            ("cn", [name]), ("sAMAccountName", [name + "$"]), ("dNSHostName", [name.ToLowerInvariant() + "." + Domain]),
            ("objectSid", [DomainSid + "-" + (nextRid++).ToString(CultureInfo.InvariantCulture)]),
            ("userAccountControl", ["4096"]),
        };
        attributes.AddRange(extra.Select(e => (e.Name, (IReadOnlyList<string>)[e.Value])));
        var o = Obj("CN=" + name + "," + ou, [.. attributes]);
        Objects.Add(o);
        return o;
    }

    public DirectoryObject AddGroup(string dn, string sam, int? rid = null, string? sid = null, bool adminCount = false)
    {
        var attributes = new List<(string, IReadOnlyList<string>)>
        {
            ("objectClass", ["top", "group"]),
            ("cn", [DistinguishedName.FirstRdn(dn).Value]), ("sAMAccountName", [sam]),
            ("objectSid", [sid ?? DomainSid + "-" + (rid ?? nextRid++).ToString(CultureInfo.InvariantCulture)]),
        };
        if (adminCount)
        {
            attributes.Add(("adminCount", ["1"]));
        }

        var o = Obj(dn, [.. attributes]);
        Objects.Add(o);
        return o;
    }

    public static DirectoryObject Obj(string dn, params (string Name, IReadOnlyList<string> Values)[] attributes) =>
        new(dn, attributes.Select(a => new KeyValuePair<string, IReadOnlyList<string>>(a.Name, a.Values)));

    // ---- IUniquenessChecker over the whole domain ----
    public bool IsSamAccountNameTaken(string sam) => Any("sAMAccountName", sam);

    public bool IsUpnTaken(string upn) => Any("userPrincipalName", upn);

    public bool IsMailTaken(string address) =>
        Any("mail", address) || Objects.Any(o => o.GetAll("proxyAddresses").Any(p =>
            p.StartsWith("smtp:", StringComparison.OrdinalIgnoreCase) && string.Equals(p[5..], address, StringComparison.OrdinalIgnoreCase)));

    public bool IsCnTaken(string cn) => Any("cn", cn);

    public bool IsDnTaken(string dn) => Objects.Any(o => DistinguishedName.AreEqual(o.Dn, dn));

    public bool IsDnsHostNameTaken(string host) => Any("dNSHostName", host);

    private bool Any(string attribute, string value) =>
        Objects.Any(o => o.GetAll(attribute).Contains(value, StringComparer.OrdinalIgnoreCase));
}
