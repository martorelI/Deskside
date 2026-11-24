using Deckside.Core.Directory;
using Deckside.Core.Profiles;

namespace Deckside.Core.Plans;

/// <summary>Exactly what "Assign / change profile" will do to one user or computer.</summary>
public sealed record ProfileChangePlan
{
    public required string ObjectDn { get; init; }

    public string SamAccountName { get; init; } = "";

    public IReadOnlyList<string> Profiles { get; init; } = [];

    public GroupDifference Difference { get; init; } = new([], [], [], []);

    public IReadOnlyList<PlanIssue> Issues { get; init; } = [];

    public bool CanExecute => Issues.All(i => i.Severity != IssueSeverity.Error);
}

public static class ProfileChangePlanner
{
    /// <param name="target">The user or computer as read right now.</param>
    /// <param name="primaryGroupDn">DN of the group of its <c>primaryGroupID</c> (never touched), if known.</param>
    public static ProfileChangePlan Plan(DirectoryObject target, IReadOnlyList<string> profileNames, string? primaryGroupDn, PlanningContext ctx)
    {
        var t = ctx.T;
        var issues = new IssueList();
        var kind = target.IsComputer ? ProfileTarget.Computer : ProfileTarget.User;
        var plan = new ProfileChangePlan { ObjectDn = target.Dn, SamAccountName = target.SamAccountName ?? "", Profiles = profileNames };

        if (!target.IsUser && !target.IsComputer)
        {
            issues.Error("NOT_A_USER", t.T("{0} is not a user account.", target.Dn));
        }

        if (ProtectedObjects.IsProtectedAccount(target))
        {
            issues.Error("PROTECTED_ACCOUNT", t.T("Protected account (adminCount or built-in), Deckside never changes it: {0}", target.Dn));
        }

        var area = kind == ProfileTarget.Computer ? ScopeArea.Computers : ScopeArea.Users;
        if (!ctx.Scope.Contains(area, target.Dn))
        {
            issues.Error("OUTSIDE_SCOPE", t.T("The object is outside the configured OU, it can be read but not changed: {0}", target.Dn));
        }

        if (profileNames.Count == 0)
        {
            issues.Error("PROFILE_REQUIRED", t.T("Choose at least one profile."));
        }

        ctx.GroupsOfProfiles(profileNames, kind, issues);
        if (issues.HasErrors)
        {
            return plan with { Issues = issues.Items };
        }

        var profiles = profileNames.Select(n => ctx.Profiles.Find(n)!).ToList();
        var diff = ProfileDiff.Compute(target.GetAll("memberOf"), profiles, ctx.Profiles, primaryGroupDn);

        // Every group that changes is checked again here: the directory may have changed since the profiles were loaded.
        ctx.CheckGroupsToAdd(diff.ToAdd.Concat(diff.ToRemove), issues);
        if (diff.IsEmpty)
        {
            issues.Info("NO_CHANGES", t.T("The object already has exactly the groups of the profile."));
        }

        return plan with { Difference = diff, Issues = issues.Items };
    }
}
