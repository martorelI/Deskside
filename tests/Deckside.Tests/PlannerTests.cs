using Deckside.Core.Directory;
using Deckside.Core.Localization;
using Deckside.Core.Plans;
using Deckside.Core.Text;
using Deckside.Tests.Support;

namespace Deckside.Tests;

public class CreateUserPlannerTests
{
    private static CreateUserRequest Request(string given = "José Luis", string sn1 = "Peña", string sn2 = "Gómez") => new()
    {
        GivenNames = given,
        Surname1 = sn1,
        Surname2 = sn2,
        EmployeeId = "E-1001",
        Department = "Finance",
        Title = "Analyst",
        Ticket = "INC-1",
        Profiles = ["Accounting"],
        Extra = new Dictionary<string, string> { ["telephoneNumber"] = "+52 55 0000 0000" },
    };

    private static CreateUserPlan Plan(TestDomain d, CreateUserRequest r, string language = "en") =>
        CreateUserPlanner.Plan(r, d.SamNames("U"), d.Context(language: language));

    [Fact]
    public void Next_sequential_comes_from_the_whole_domain_not_only_the_ou()
    {
        var d = TestDomain.Standard();
        var plan = Plan(d, Request());
        Assert.True(plan.CanExecute, string.Join("\n", plan.Issues));
        // U00050 lives outside the configured OUs and still counts.
        Assert.Equal("U00051", plan.SamAccountName);
        Assert.Equal("U00050", plan.Sequence!.Previous!.Name);
        Assert.Equal("jose.pena@example.local", plan.Mail);
        Assert.Equal("U00051@example.local", plan.UserPrincipalName);
        Assert.Equal("CN=José Luis Peña Gómez," + TestDomain.UsersOu, plan.Dn);
        Assert.Equal([TestDomain.Group("GG-Accounting"), TestDomain.Group("GG-VPN")], plan.GroupsToAdd);
        Assert.Equal("514", plan.Attributes.Single(a => a.Name == "userAccountControl").After[0]);
        Snapshot.Match("plan.create-user.txt", PlanText.Render(plan, new Translator("en")));
        Snapshot.Match("plan.create-user.es.txt", PlanText.Render(Plan(d, Request(), "es"), new Translator("es")));
    }

    [Fact]
    public void Mail_in_proxy_addresses_or_contacts_counts_as_taken()
    {
        var d = TestDomain.Standard();
        var fromProxy = Plan(d, Request("Mateo", "Ruiz", "") with { Profiles = [] });
        Assert.Equal("mateo.ruiz@example.local", fromProxy.Mail);
        var maria = Plan(d, Request("María", "Ruiz", "Soto") with { Profiles = [] });
        Assert.Equal("maria.ruizs@example.local", maria.Mail);
        Assert.Contains(maria.Issues, i => i.Code == "MAIL_FALLBACK");
        var juan = Plan(d, Request("Juan", "Pérez", "") with { Profiles = [] });
        Assert.Equal("juan.perez2@example.local", juan.Mail);
    }

    [Fact]
    public void Existing_cn_gets_the_account_name_appended()
    {
        var d = TestDomain.Standard();
        var plan = Plan(d, Request("Ana", "Lopez", "") with { Profiles = [] });
        Assert.Equal("Ana Lopez (U00051)", plan.Cn);
        Assert.Contains(plan.Issues, i => i.Code == "CN_FALLBACK");
    }

    [Fact]
    public void A_profile_with_a_protected_group_blocks_the_plan()
    {
        var d = TestDomain.Standard();
        var catalog = new Deckside.Core.Profiles.ProfileCatalog(
        [
            new("Evil", Deckside.Core.Profiles.ProfileTarget.User, ["CN=Domain Admins,CN=Users," + TestDomain.Base]),
            new("Sneaky", Deckside.Core.Profiles.ProfileTarget.User, [TestDomain.Group("Backup Ops Copy")]),
        ]);
        var outside = CreateUserPlanner.Plan(Request() with { Profiles = ["Evil"] }, d.SamNames("U"), d.Context(profiles: catalog));
        Assert.False(outside.CanExecute);
        Assert.Contains(outside.Issues, i => i.Code == "GROUP_OUTSIDE_MASTER_OU");
        var inside = CreateUserPlanner.Plan(Request() with { Profiles = ["Sneaky"] }, d.SamNames("U"), d.Context(profiles: catalog));
        Assert.False(inside.CanExecute);
        Assert.Contains(inside.Issues, i => i.Code == "PROTECTED_GROUP");
    }

    [Fact]
    public void Outside_scope_unconfigured_ou_and_forbidden_attributes()
    {
        var d = TestDomain.Standard();
        var outside = Plan(d, Request() with { TargetOu = "OU=Other," + TestDomain.Base });
        Assert.Contains(outside.Issues, i => i.Code == "OUTSIDE_SCOPE");
        var sub = Plan(d, Request() with { TargetOu = "OU=Sales," + TestDomain.UsersOu });
        Assert.True(sub.CanExecute);

        var noOu = CreateUserPlanner.Plan(Request(), d.SamNames("U"), d.Context(TestDomain.Settings with { UsersOU = "" }));
        Assert.Contains(noOu.Issues, i => i.Code == "OU_NOT_CONFIGURED");

        var attr = Plan(d, Request() with { Extra = new Dictionary<string, string> { ["userAccountControl"] = "66048", ["scriptPath"] = "x.bat" } });
        Assert.Equal(2, attr.Issues.Count(i => i.Code == "ATTRIBUTE_NOT_ALLOWED"));
        Assert.False(attr.CanExecute);
    }

    [Fact]
    public void Required_fields_and_invalid_names()
    {
        var d = TestDomain.Standard();
        Assert.Contains(Plan(d, Request("", "Peña")).Issues, i => i.Code == "NAME_REQUIRED");
        Assert.Contains(Plan(d, Request("Ana3", "Peña")).Issues, i => i.Code == "NAME_INVALID_CHARACTERS");
        Assert.Contains(Plan(d, Request() with { Ticket = "" }).Issues, i => i.Code == "TICKET_REQUIRED");
    }

    [Fact]
    public void A_race_on_the_number_skips_to_the_next_one()
    {
        var d = TestDomain.Standard();
        // Somebody created U00051 with a name the prefix search did not return (e.g. created a second ago).
        var plan = CreateUserPlanner.Plan(Request(), d.SamNames("U").Where(n => n.Name != "U00050"), d.Context());
        Assert.Equal("U00042", plan.SamAccountName);
        d.AddUser(TestDomain.UsersOu, "U00042", "Someone", "Else");
        var again = CreateUserPlanner.Plan(Request(), d.SamNames("U").Where(n => n.Name is not ("U00050" or "U00042")), d.Context());
        Assert.Equal("U00043", again.SamAccountName);
        Assert.Contains(again.Issues, i => i.Code == "SEQUENCE_SKIPPED");
    }
}

public class CreateComputerPlannerTests
{
    [Fact]
    public void Sequential_name_default_groups_and_profile()
    {
        var d = TestDomain.Standard();
        var plan = CreateComputerPlanner.Plan(new CreateComputerRequest { Ticket = "INC-2", Profiles = ["Kiosk PC"], Description = "Front desk" },
            d.SamNames("PC"), d.Context());
        Assert.True(plan.CanExecute, string.Join("\n", plan.Issues));
        Assert.Equal("PC00021", plan.Name);
        Assert.Equal("PC00021$", plan.SamAccountName);
        Assert.Equal([TestDomain.Group("GG-Kiosk"), TestDomain.Group("GG-Printers")], plan.GroupsToAdd.Order());
        Assert.Equal("4128", plan.Attributes.Single(a => a.Name == "userAccountControl").After[0]);
        Snapshot.Match("plan.create-computer.txt", PlanText.Render(plan, new Translator("en")));
    }

    [Fact]
    public void Typed_name_taken_invalid_or_protected_group()
    {
        var d = TestDomain.Standard();
        var taken = CreateComputerPlanner.Plan(new CreateComputerRequest { Name = "pc00009", Ticket = "1" }, [], d.Context());
        Assert.Contains(taken.Issues, i => i.Code == "NAME_TAKEN");
        var invalid = CreateComputerPlanner.Plan(new CreateComputerRequest { Name = "PC_TOO_LONG_NAME_X", Ticket = "1" }, [], d.Context());
        Assert.Contains(invalid.Issues, i => i.Code == "COMPUTER_NAME_INVALID");
        var evil = CreateComputerPlanner.Plan(new CreateComputerRequest { Name = "PC-NEW", Ticket = "1", Groups = ["CN=Administrators,CN=Builtin," + TestDomain.Base] }, [], d.Context());
        Assert.Contains(evil.Issues, i => i.Code == "GROUP_OUTSIDE_MASTER_OU");
        var tier0 = CreateComputerPlanner.Plan(new CreateComputerRequest { Name = "PC-NEW", Ticket = "1", Groups = [TestDomain.Group("GG-Tier0")] }, [], d.Context());
        Assert.Contains(tier0.Issues, i => i.Code == "PROTECTED_GROUP");
        var userProfile = CreateComputerPlanner.Plan(new CreateComputerRequest { Name = "PC-NEW", Ticket = "1", Profiles = ["Accounting"] }, [], d.Context());
        Assert.Contains(userProfile.Issues, i => i.Code == "PROFILE_WRONG_TARGET");
    }
}

public class OffboardingPlannerTests
{
    private static OffboardingRequest Request(bool managed = true, bool all = false) => new()
    {
        Reason = "Left the company",
        Ticket = "INC-3",
        Operator = "EXAMPLE\\operator1",
        RemoveManagedGroups = managed,
        RemoveAllGroups = all,
    };

    [Fact]
    public void Disable_move_and_remove_only_managed_groups()
    {
        var d = TestDomain.Standard();
        var user = d.Get("U00041");
        var plan = OffboardingPlanner.Plan(user, d.GroupsOf(user), Request(), d.Context());
        Assert.True(plan.CanExecute, string.Join("\n", plan.Issues));
        Assert.Equal("CN=Carla Mendez," + TestDomain.DisabledOu, plan.NewDn);
        Assert.Equal("OU=Sales," + TestDomain.UsersOu, plan.SourceOu);
        Assert.Equal(512, plan.AccountControlBefore);
        Assert.Equal(514, plan.AccountControlAfter);
        Assert.Equal([TestDomain.Group("GG-Warehouse")], plan.GroupsToRemove);
        Assert.Equal(2, plan.GroupsBefore.Count);
        Assert.Equal("Disabled 2026-11-24 | INC-3 | Left the company | EXAMPLE\\operator1", plan.DescriptionAfter);
        Snapshot.Match("plan.offboarding.txt", PlanText.Render(plan, new Translator("en")));
    }

    [Fact]
    public void Remove_all_never_removes_protected_groups()
    {
        var d = TestDomain.Standard();
        var user = d.AddUser(TestDomain.UsersOu, "U00060", "Dora", "Vega", groups: [TestDomain.Group("GG-Projects"), TestDomain.Group("GG-Tier0")]);
        var plan = OffboardingPlanner.Plan(user, d.GroupsOf(user), Request(all: true), d.Context());
        Assert.Equal([TestDomain.Group("GG-Projects")], plan.GroupsToRemove);
        Assert.Contains(plan.Issues, i => i.Code == "PROTECTED_GROUP_KEPT");
    }

    [Fact]
    public void Refuses_protected_out_of_scope_and_already_offboarded_accounts()
    {
        var d = TestDomain.Standard();
        Assert.Contains(OffboardingPlanner.Plan(d.Get("U00030"), [], Request(), d.Context()).Issues, i => i.Code == "PROTECTED_ACCOUNT");
        Assert.Contains(OffboardingPlanner.Plan(d.Get("U00050"), [], Request(), d.Context()).Issues, i => i.Code == "OUTSIDE_SCOPE");
        var gone = d.AddUser(TestDomain.DisabledOu, "U00061", "Eva", "Sol", uac: 514);
        Assert.Contains(OffboardingPlanner.Plan(gone, [], Request(), d.Context()).Issues, i => i.Code == "ALREADY_OFFBOARDED");
        Assert.Contains(OffboardingPlanner.Plan(d.Get("PC00003$"), [], Request(), d.Context()).Issues, i => i.Code == "NOT_A_USER");
        Assert.Contains(OffboardingPlanner.Plan(d.Get("U00001"), [], Request() with { Reason = " " }, d.Context()).Issues, i => i.Code == "REASON_REQUIRED");
    }

    [Fact]
    public void Sub_ous_out_of_scope_when_configured_so()
    {
        var d = TestDomain.Standard();
        var ctx = d.Context(TestDomain.Settings with { IncludeSubOUs = false });
        var user = d.Get("U00041");
        Assert.Contains(OffboardingPlanner.Plan(user, d.GroupsOf(user), Request(), ctx).Issues, i => i.Code == "OUTSIDE_SCOPE");
    }

    [Fact]
    public void Name_collision_in_the_disabled_ou()
    {
        var d = TestDomain.Standard();
        d.AddUser(TestDomain.DisabledOu, "U00002", "Bruno", "Diaz", uac: 514);
        var plan = OffboardingPlanner.Plan(d.Get("U00007"), [], Request(), d.Context());
        Assert.Contains(plan.Issues, i => i.Code == "DN_TAKEN");
    }
}

public class ProfileChangePlannerTests
{
    [Fact]
    public void Change_profile_preview()
    {
        var d = TestDomain.Standard();
        var plan = ProfileChangePlanner.Plan(d.Get("U00041"), ["Accounting"], "CN=Domain Users,CN=Users," + TestDomain.Base, d.Context());
        Assert.True(plan.CanExecute, string.Join("\n", plan.Issues));
        Assert.Equal([TestDomain.Group("GG-Accounting"), TestDomain.Group("GG-VPN")], plan.Difference.ToAdd);
        Assert.Equal([TestDomain.Group("GG-Warehouse")], plan.Difference.ToRemove);
        Assert.Equal([TestDomain.Group("GG-Projects")], plan.Difference.Untouched);
        Snapshot.Match("plan.profile.txt", PlanText.Render(plan, new Translator("en")));
    }

    [Fact]
    public void Refuses_protected_out_of_scope_and_wrong_kind()
    {
        var d = TestDomain.Standard();
        Assert.Contains(ProfileChangePlanner.Plan(d.Get("U00030"), ["Accounting"], null, d.Context()).Issues, i => i.Code == "PROTECTED_ACCOUNT");
        Assert.Contains(ProfileChangePlanner.Plan(d.Get("U00050"), ["Accounting"], null, d.Context()).Issues, i => i.Code == "OUTSIDE_SCOPE");
        Assert.Contains(ProfileChangePlanner.Plan(d.Get("PC00003$"), ["Accounting"], null, d.Context()).Issues, i => i.Code == "PROFILE_WRONG_TARGET");
        Assert.True(ProfileChangePlanner.Plan(d.Get("PC00003$"), ["Kiosk PC"], null, d.Context()).CanExecute);
        Assert.Contains(ProfileChangePlanner.Plan(d.Get("PC00020$"), ["Kiosk PC"], null, d.Context()).Issues, i => i.Code == "OUTSIDE_SCOPE");
        Assert.Contains(ProfileChangePlanner.Plan(d.Get("U00001"), ["Accounting"], null, d.Context()).Issues, i => i.Code == "NO_CHANGES");
    }

    [Fact]
    public void A_group_that_became_protected_after_loading_blocks_the_change()
    {
        var d = TestDomain.Standard();
        var ctx = d.Context(TestDomain.Settings with { ExtraProtectedGroups = ["GG-Warehouse"] });
        var plan = ProfileChangePlanner.Plan(d.Get("U00001"), ["Warehouse"], null, ctx);
        Assert.False(plan.CanExecute);
        Assert.Contains(plan.Issues, i => i.Code == "PROTECTED_GROUP");
    }
}
