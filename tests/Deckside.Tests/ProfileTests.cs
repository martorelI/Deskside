using Deckside.Core.Profiles;
using Deckside.Tests.Support;

namespace Deckside.Tests;

public class ProfileFileTests
{
    [Fact]
    public void Parses_and_reports_bad_entries()
    {
        var json = $$"""
        [
          { "Name": "Accounting", "Applies": "User", "Groups": ["{{TestDomain.Group("GG-Accounting")}}", "{{TestDomain.Group("GG-VPN")}}"] },
          { "Name": "accounting", "Applies": "User", "Groups": ["{{TestDomain.Group("GG-VPN")}}"] },
          { "Name": "Bad applies", "Applies": "Printer", "Groups": ["{{TestDomain.Group("GG-VPN")}}"] },
          { "Name": "Bad dn", "Applies": "User", "Groups": ["nope"] },
          { "Name": "Empty", "Applies": "Computer", "Groups": [] },
          { "Applies": "User" }
        ]
        """;
        var (catalog, issues) = ProfileFile.Parse(json);
        Assert.Equal("Accounting", Assert.Single(catalog.Profiles).Name);
        var codes = issues.Select(i => i.Code).ToList();
        Assert.Equal(["DUPLICATED_NAME", "INVALID_APPLIES", "INVALID_GROUP_DN", "MISSING_GROUPS", "MISSING_NAME"], codes.Order());
        Assert.True(catalog.IsManaged("cn=gg-vpn," + TestDomain.GroupsOu.ToUpperInvariant()));
    }

    [Fact]
    public void A_profile_with_a_protected_or_missing_group_is_rejected_when_resolved()
    {
        var d = TestDomain.Standard();
        var catalog = new ProfileCatalog(
        [
            new GroupProfile("Good", ProfileTarget.User, [TestDomain.Group("GG-VPN")]),
            new GroupProfile("Sneaky", ProfileTarget.User, [TestDomain.Group("GG-Projects"), "CN=Domain Admins,CN=Users," + TestDomain.Base]),
            new GroupProfile("Tier0", ProfileTarget.User, [TestDomain.Group("GG-Tier0")]),
            new GroupProfile("Dns", ProfileTarget.User, ["CN=DnsAdmins,CN=Users," + TestDomain.Base]),
            new GroupProfile("Ghost", ProfileTarget.User, [TestDomain.Group("GG-Nobody")]),
            new GroupProfile("User as group", ProfileTarget.User, ["CN=Ana Lopez," + TestDomain.UsersOu]),
        ]);
        var (resolved, issues) = catalog.Resolve(dn => d.Objects.FirstOrDefault(o => Deckside.Core.Directory.DistinguishedName.AreEqual(o.Dn, dn)), TestDomain.Root);
        Assert.Equal("Good", Assert.Single(resolved.Profiles).Name);
        Assert.Equal(["GROUP_NOT_FOUND", "NOT_A_GROUP", "OUTSIDE_MASTER_OU", "OUTSIDE_MASTER_OU", "PROTECTED_GROUP"], issues.Select(i => i.Code).Order());
        // Groups of rejected profiles stay managed (they are removed when a user changes away from them).
        Assert.True(resolved.IsManaged(TestDomain.Group("GG-Projects")));
    }
}

public class ProfileDiffTests
{
    [Fact]
    public void Only_managed_groups_change()
    {
        var catalog = TestDomain.Profiles();
        var current = new[] { TestDomain.Group("GG-Accounting"), TestDomain.Group("GG-VPN"), TestDomain.Group("GG-Projects") };
        var diff = ProfileDiff.Compute(current, [catalog.Find("Warehouse")!], catalog);
        Assert.Equal([TestDomain.Group("GG-Warehouse")], diff.ToAdd);
        Assert.Equal([TestDomain.Group("GG-Accounting"), TestDomain.Group("GG-VPN")], diff.ToRemove);
        Assert.Empty(diff.Keep);
        Assert.Equal([TestDomain.Group("GG-Projects")], diff.Untouched);
    }

    [Fact]
    public void Several_profiles_at_once_and_same_profile_is_empty()
    {
        var catalog = TestDomain.Profiles();
        var current = new[] { TestDomain.Group("GG-Accounting"), TestDomain.Group("GG-VPN") };
        var both = ProfileDiff.Compute(current, [catalog.Find("Accounting")!, catalog.Find("Warehouse")!], catalog);
        Assert.Equal([TestDomain.Group("GG-Warehouse")], both.ToAdd);
        Assert.Empty(both.ToRemove);
        Assert.True(ProfileDiff.Compute(current, [catalog.Find("accounting")!], catalog).IsEmpty);
    }

    [Fact]
    public void The_primary_group_is_never_added_or_removed()
    {
        var domainUsers = "CN=Domain Users,CN=Users," + TestDomain.Base;
        var catalog = new ProfileCatalog([new GroupProfile("Base", ProfileTarget.User, [domainUsers, TestDomain.Group("GG-VPN")])]);
        var diff = ProfileDiff.Compute([], catalog.Profiles, catalog, primaryGroupDn: domainUsers);
        Assert.Equal([TestDomain.Group("GG-VPN")], diff.ToAdd);
        Assert.Contains(domainUsers, diff.Keep);
    }
}
