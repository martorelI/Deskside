using Deckside.Core.Directory;
using Deckside.Tests.Support;

namespace Deckside.Tests;

public class DistinguishedNameTests
{
    [Fact]
    public void Parses_escaped_values_and_compares_ignoring_case_and_escaping()
    {
        var rdns = DistinguishedName.Parse(@"CN=Perez\, Juan,OU=Staff,DC=example,DC=local");
        Assert.Equal("Perez, Juan", rdns[0].Value);
        Assert.Equal(4, rdns.Count);
        Assert.True(DistinguishedName.AreEqual(@"CN=Perez\, Juan,OU=Staff,DC=example,DC=local", @"cn=PEREZ\2C JUAN, ou=staff, dc=EXAMPLE, dc=local"));
    }

    [Fact]
    public void Hex_escapes_are_utf8()
    {
        Assert.Equal("Peña", DistinguishedName.FirstRdn(@"CN=Pe\C3\B1a,DC=example,DC=local").Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("CN=")]
    [InlineData("nothing")]
    [InlineData("CN=a,")]
    [InlineData("CN=a+UID=b,DC=x")]
    public void Rejects_invalid_dns(string dn) => Assert.False(DistinguishedName.IsValid(dn));

    [Fact]
    public void Descendant_and_child()
    {
        Assert.True(DistinguishedName.IsDescendantOf("CN=a,OU=Sales," + TestDomain.UsersOu, TestDomain.UsersOu));
        Assert.False(DistinguishedName.IsChildOf("CN=a,OU=Sales," + TestDomain.UsersOu, TestDomain.UsersOu));
        Assert.True(DistinguishedName.IsChildOf("CN=a," + TestDomain.UsersOu, TestDomain.UsersOu));
        Assert.False(DistinguishedName.IsDescendantOf(TestDomain.UsersOu, TestDomain.UsersOu));
        // "OU=Staff2" is not inside "OU=Staff": the comparison is per component, not per string.
        Assert.False(DistinguishedName.IsDescendantOf("CN=a,OU=Staff2,OU=Deckside Test," + TestDomain.Base, TestDomain.UsersOu));
        Assert.False(DistinguishedName.IsDescendantOf("CN=a,OU=Staff,OU=Deckside Test,DC=evil,DC=example,DC=local", TestDomain.UsersOu));
    }

    [Theory]
    [InlineData("Perez, Juan", @"Perez\, Juan")]
    [InlineData("#tag", @"\#tag")]
    [InlineData(" lead", @"\ lead")]
    [InlineData("trail ", @"trail\ ")]
    [InlineData("a=b+c", @"a\=b\+c")]
    public void Escapes_values_and_round_trips(string value, string escaped)
    {
        Assert.Equal(escaped, DistinguishedName.EscapeValue(value));
        Assert.Equal(value, DistinguishedName.FirstRdn(DistinguishedName.Child("CN", value, TestDomain.UsersOu)).Value);
    }

    [Fact]
    public void Parent_and_domain()
    {
        Assert.Equal(TestDomain.UsersOu, DistinguishedName.Parent("CN=x," + TestDomain.UsersOu));
        Assert.Equal("example.local", DistinguishedName.DomainOf(TestDomain.UsersOu));
    }
}

public class AdTimeTests
{
    [Fact]
    public void Filetime_never_and_zero_are_null()
    {
        Assert.Null(AdTime.FromFileTime(0));
        Assert.Null(AdTime.FromFileTime(long.MaxValue));
        Assert.Null(AdTime.FromFileTime("garbage"));
    }

    [Fact]
    public void Filetime_round_trip()
    {
        var value = AdTime.ToFileTime(TestDomain.Now);
        Assert.Equal(TestDomain.Now, AdTime.FromFileTime(value));
    }

    [Fact]
    public void Generalized_time()
    {
        Assert.Equal(new DateTimeOffset(2024, 1, 31, 23, 59, 58, TimeSpan.Zero), AdTime.FromGeneralizedTime("20240131235958.0Z"));
        Assert.Equal("20261124150000.0Z", AdTime.ToGeneralizedTime(TestDomain.Now));
    }
}

public class ProtectedObjectsTests
{
    [Theory]
    [InlineData("S-1-5-21-1000-2000-3000-512", true)]  // Domain Admins
    [InlineData("S-1-5-21-1000-2000-3000-519", true)]  // Enterprise Admins
    [InlineData("S-1-5-21-1000-2000-3000-518", true)]  // Schema Admins
    [InlineData("S-1-5-21-1000-2000-3000-520", true)]  // Group Policy Creator Owners
    [InlineData("S-1-5-21-1000-2000-3000-526", true)]  // Key Admins
    [InlineData("S-1-5-32-544", true)]                 // Administrators
    [InlineData("S-1-5-32-548", true)]                 // Account Operators
    [InlineData("S-1-5-32-551", true)]                 // Backup Operators
    [InlineData("S-1-5-21-1000-2000-3000-513", false)] // Domain Users
    [InlineData("S-1-5-21-1000-2000-3000-1105", false)]
    [InlineData("S-1-5-32-545", false)]                // Users
    [InlineData("", false)]
    public void Protected_group_by_sid(string sid, bool expected) => Assert.Equal(expected, ProtectedObjects.IsProtectedGroupSid(sid));

    [Fact]
    public void Renamed_domain_admins_is_still_protected_and_dnsadmins_by_name()
    {
        var renamed = TestDomain.Obj("CN=Admins del dominio,CN=Users," + TestDomain.Base, ("objectClass", ["group"]),
            ("sAMAccountName", ["Admins. del dominio"]), ("objectSid", [TestDomain.DomainSid + "-512"]));
        Assert.True(ProtectedObjects.IsProtectedGroup(renamed));
        var d = TestDomain.Standard();
        Assert.True(ProtectedObjects.IsProtectedGroup(d.Get("DnsAdmins")));
        Assert.True(ProtectedObjects.IsProtectedGroup(d.Get("GG-Tier0")));
        Assert.False(ProtectedObjects.IsProtectedGroup(d.Get("GG-VPN")));
        Assert.True(ProtectedObjects.IsProtectedGroup(d.Get("GG-VPN"), ["gg-vpn"]));
    }

    [Fact]
    public void Protected_accounts()
    {
        var d = TestDomain.Standard();
        Assert.True(ProtectedObjects.IsProtectedAccount(d.Get("U00030")));
        Assert.False(ProtectedObjects.IsProtectedAccount(d.Get("U00001")));
        var krbtgt = TestDomain.Obj("CN=krbtgt,CN=Users," + TestDomain.Base, ("objectClass", ["user"]), ("objectSid", [TestDomain.DomainSid + "-502"]));
        Assert.True(ProtectedObjects.IsProtectedAccount(krbtgt));
    }
}
