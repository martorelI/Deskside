using Deckside.Core.Plans;
using Deckside.Directory;
using Deckside.Tests.Support;

namespace Deckside.Tests;

/// <summary>The last gate: nothing reaches the directory outside the master OU.</summary>
public class GuardedDirectoryTests
{
    private static readonly string User = "CN=Ana Lopez," + TestDomain.UsersOu;
    private static readonly string Outside = "CN=Outside Scope,OU=Other," + TestDomain.Base;

    private static (GuardedDirectory Guard, RecordingDirectory Inner) Make(string master = TestDomain.Root)
    {
        var inner = new RecordingDirectory(TestDomain.Standard());
        return (new GuardedDirectory(inner, master), inner);
    }

    private static string Code(Action action) => Assert.Throws<DirectoryException>(action).Code;

    [Fact]
    public void Writes_inside_the_master_ou_pass()
    {
        var (g, inner) = Make();
        g.Add("CN=New," + TestDomain.UsersOu, [AttributeChange.Set("objectClass", "user"), AttributeChange.Set("userAccountControl", "514")]);
        g.Modify(User, [AttributeModification.Replace("userAccountControl", "514")]);
        g.Move(User, TestDomain.DisabledOu);
        g.AddMember(TestDomain.Group("GG-VPN"), User);
        g.SetPassword(User, "x");
        Assert.Equal(5, inner.Writes.Count);
    }

    [Fact]
    public void Writes_outside_the_master_ou_never_reach_the_directory()
    {
        var (g, inner) = Make();
        Assert.Equal("OUTSIDE_MASTER_OU", Code(() => g.Add("CN=New,OU=Other," + TestDomain.Base, [])));
        Assert.Equal("OUTSIDE_MASTER_OU", Code(() => g.Modify(Outside, [AttributeModification.Replace("description", "x")])));
        Assert.Equal("OUTSIDE_MASTER_OU", Code(() => g.Move(User, "OU=Other," + TestDomain.Base)));
        Assert.Equal("OUTSIDE_MASTER_OU", Code(() => g.Move(Outside, TestDomain.DisabledOu)));
        Assert.Equal("OUTSIDE_MASTER_OU", Code(() => g.AddMember("CN=Domain Admins,CN=Users," + TestDomain.Base, User)));
        Assert.Equal("OUTSIDE_MASTER_OU", Code(() => g.AddMember(TestDomain.Group("GG-VPN"), Outside)));
        Assert.Equal("OUTSIDE_MASTER_OU", Code(() => g.RemoveMember(TestDomain.Group("GG-VPN"), Outside)));
        Assert.Equal("OUTSIDE_MASTER_OU", Code(() => g.SetPassword(Outside, "x")));
        // The master OU itself is not "inside": it cannot be changed or moved.
        Assert.Equal("OUTSIDE_MASTER_OU", Code(() => g.Modify(TestDomain.Root, [AttributeModification.Replace("description", "x")])));
        // A look-alike name is not inside (comparison per DN component).
        Assert.Equal("OUTSIDE_MASTER_OU", Code(() => g.Add("CN=x,OU=Deckside Test2," + TestDomain.Base, [])));
        Assert.Empty(inner.Writes);
    }

    [Fact]
    public void No_master_ou_wrong_domain_or_missing_master_blocks_every_write()
    {
        var (none, _) = Make("");
        Assert.Equal("MASTER_OU_NOT_CONFIGURED", Code(() => none.Modify(User, [])));
        var (other, _) = Make("OU=Deckside Test,DC=other,DC=local");
        Assert.Equal("MASTER_OU_NOT_IN_DOMAIN", Code(() => other.Modify("CN=x,OU=Deckside Test,DC=other,DC=local", [])));
        var (missing, inner) = Make();
        inner.MasterExists = false;
        Assert.Equal("MASTER_OU_NOT_FOUND", Code(() => missing.Modify(User, [])));
        Assert.Empty(inner.Writes);
        // Reads still work.
        Assert.NotNull(missing.Get(User, ["cn"]));
    }

    [Fact]
    public void Security_attributes_and_dangerous_flags_are_refused()
    {
        var (g, inner) = Make();
        foreach (var attribute in new[] { "servicePrincipalName", "nTSecurityDescriptor", "member", "adminCount", "sIDHistory", "msDS-AllowedToActOnBehalfOfOtherIdentity", "unicodePwd", "scriptPath" })
        {
            Assert.Equal("ATTRIBUTE_FORBIDDEN", Code(() => g.Modify(User, [AttributeModification.Replace(attribute, "x")])));
        }

        Assert.Equal("ATTRIBUTE_FORBIDDEN", Code(() => g.Modify(User, [AttributeModification.Replace("objectClass", "computer")])));
        Assert.Equal("UAC_FORBIDDEN", Code(() => g.Modify(User, [AttributeModification.Replace("userAccountControl", "524800")]))); // trusted for delegation
        Assert.Equal("UAC_FORBIDDEN", Code(() => g.Modify(User, [AttributeModification.Replace("userAccountControl", "4194816")]))); // no Kerberos preauth
        Assert.Equal("UAC_FORBIDDEN", Code(() => g.Add("CN=New," + TestDomain.UsersOu, [AttributeChange.Set("objectClass", "user"), AttributeChange.Set("userAccountControl", "544")])));
        Assert.Equal("UAC_INVALID", Code(() => g.Modify(User, [AttributeModification.Replace("userAccountControl", "abc")])));
        // Pre-staged computers do carry PASSWD_NOTREQD (4128).
        g.Add("CN=PC1," + TestDomain.ComputersOu, [AttributeChange.Set("objectClass", "computer"), AttributeChange.Set("userAccountControl", "4128")]);
        Assert.Single(inner.Writes);
    }
}

public class DirectoryHelpersTests
{
    [Fact]
    public void Filter_values_are_escaped()
    {
        Assert.Equal(@"(sAMAccountName=a\2a\29\28\5c)", LdapFilter.Equal("sAMAccountName", @"a*)(\"));
        Assert.Equal("(sAMAccountName=U*)", LdapFilter.StartsWith("sAMAccountName", "U"));
        Assert.Equal("(&(a=1)(b=2))", LdapFilter.And("(a=1)", "(b=2)"));
    }

    [Fact]
    public void Binary_sid_and_guid()
    {
        // S-1-5-21-1000-2000-3000-512
        byte[] sid = [1, 5, 0, 0, 0, 0, 0, 5, 21, 0, 0, 0, 0xE8, 3, 0, 0, 0xD0, 7, 0, 0, 0xB8, 0x0B, 0, 0, 0, 2, 0, 0];
        Assert.Equal("S-1-5-21-1000-2000-3000-512", BinaryValues.SidToString(sid));
        Assert.Equal("S-1-5-32-544", BinaryValues.SidToString([1, 2, 0, 0, 0, 0, 0, 5, 32, 0, 0, 0, 0x20, 2, 0, 0]));
        Assert.Throws<FormatException>(() => BinaryValues.SidToString([1, 5, 0]));
        var guid = Guid.NewGuid();
        Assert.Equal(guid.ToString(), BinaryValues.ToText("objectGUID", guid.ToByteArray()));
    }
}
