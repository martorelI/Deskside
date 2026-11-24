using Deckside.Core.Plans;
using Deckside.Core.Settings;
using Deckside.Tests.Support;

namespace Deckside.Tests;

/// <summary>The master OU: nothing is written outside it.</summary>
public class MasterOuTests
{
    [Fact]
    public void Ous_outside_the_master_ou_are_dropped_when_loading()
    {
        var json = $$"""
        {
          "MasterOU": "{{TestDomain.Root}}",
          "UsersOU": "{{TestDomain.UsersOu}}",
          "ComputersOU": "OU=Computers,{{TestDomain.Base}}",
          "DisabledOU": "{{TestDomain.Base}}",
          "DefaultComputerGroups": ["{{TestDomain.Group("GG-Printers")}}", "CN=Domain Admins,CN=Users,{{TestDomain.Base}}", "{{TestDomain.Root}}"]
        }
        """;
        var r = SettingsLoader.Parse(json);
        Assert.Equal(TestDomain.UsersOu, r.Settings.UsersOU);
        Assert.Equal("", r.Settings.ComputersOU);
        Assert.Equal("", r.Settings.DisabledOU);
        Assert.Equal([TestDomain.Group("GG-Printers")], r.Settings.DefaultComputerGroups);
        Assert.Equal(4, r.Warnings.Count(w => w.Code == "OUTSIDE_MASTER_OU"));
        Assert.Equal("example.local", r.Settings.EffectiveDomain);
    }

    [Fact]
    public void Without_master_ou_nothing_is_configured()
    {
        var r = SettingsLoader.Parse($$"""{ "UsersOU": "{{TestDomain.UsersOu}}" }""");
        Assert.Equal("", r.Settings.UsersOU);
        Assert.Contains(r.Warnings, w => w.Code == "MASTER_OU_NOT_CONFIGURED");

        var scope = new Scope(TestDomain.Settings with { MasterOU = "" });
        Assert.False(scope.IsConfigured(ScopeArea.Users));
        Assert.False(scope.Contains(ScopeArea.Users, "CN=x," + TestDomain.UsersOu));
    }

    [Fact]
    public void Scope_requires_the_master_ou_even_if_the_settings_were_built_by_hand()
    {
        // Settings that did not go through the loader (e.g. built in code) are still checked by Scope.
        var s = TestDomain.Settings with { MasterOU = "OU=Staff," + TestDomain.Root };
        var scope = new Scope(s);
        Assert.True(scope.IsConfigured(ScopeArea.Users));
        Assert.False(scope.IsConfigured(ScopeArea.Computers));
        Assert.False(scope.Contains(ScopeArea.Computers, "CN=PC1," + TestDomain.ComputersOu));
        Assert.False(scope.IsInsideMaster(s.MasterOU)); // the master OU itself is never written
        Assert.True(scope.IsInsideMaster("CN=x," + s.MasterOU));
    }

    [Fact]
    public void Plans_refuse_when_the_ou_is_outside_the_master_ou()
    {
        var d = TestDomain.Standard();
        var ctx = d.Context(TestDomain.Settings with { MasterOU = "OU=Workstations," + TestDomain.Root });
        var plan = CreateUserPlanner.Plan(new CreateUserRequest { GivenNames = "Ana", Surname1 = "Paz", Ticket = "1" }, d.SamNames("U"), ctx);
        Assert.Contains(plan.Issues, i => i.Code == "OU_NOT_CONFIGURED");
        var off = OffboardingPlanner.Plan(d.Get("U00001"), [], new OffboardingRequest { Operator = "x", Reason = "r", Ticket = "1" }, ctx);
        Assert.Contains(off.Issues, i => i.Code == "OU_NOT_CONFIGURED");
    }

    [Fact]
    public void Offboarding_keeps_groups_outside_the_master_ou()
    {
        var d = TestDomain.Standard();
        var user = d.AddUser(TestDomain.UsersOu, "U00070", "Iris", "Lago", groups: [TestDomain.Group("GG-Projects"), "CN=Domain Users,CN=Users," + TestDomain.Base]);
        var plan = OffboardingPlanner.Plan(user, d.GroupsOf(user), new OffboardingRequest { Operator = "x", Reason = "r", Ticket = "1", RemoveAllGroups = true }, d.Context());
        Assert.Equal([TestDomain.Group("GG-Projects")], plan.GroupsToRemove);
        Assert.Contains(plan.Issues, i => i.Code == "GROUP_OUTSIDE_MASTER_OU");
    }

    [Theory]
    [InlineData("10.0.0.5", true)]
    [InlineData("fe80::1", true)]
    [InlineData("dc01.example.local", true)]
    [InlineData("dc01", true)]
    [InlineData("10.0.5", false)]
    [InlineData("10.0.0.300", false)]
    [InlineData("-bad", false)]
    [InlineData("", false)]
    public void Server_address(string value, bool ok) => Assert.Equal(ok, ServerAddress.IsValid(value));
}
