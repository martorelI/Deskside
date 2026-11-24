using System.Globalization;
using Deckside.Core.Directory;
using Deckside.Core.Naming;

namespace Deckside.Core.Plans;

public sealed record OffboardingRequest
{
    public string Reason { get; init; } = "";

    public string Ticket { get; init; } = "";

    /// <summary>Who runs it (the Windows account of the operator, as the directory reports it).</summary>
    public required string Operator { get; init; }

    /// <summary>Remove the groups that belong to profiles (managed groups).</summary>
    public bool RemoveManagedGroups { get; init; }

    /// <summary>Explicit checkbox: also remove every other group (never protected ones nor the primary group).</summary>
    public bool RemoveAllGroups { get; init; }
}

/// <summary>
/// Exactly what "Disable and move" will do to one account. Holds the previous state so the record can be
/// used to revert it (security rule 8).
/// </summary>
public sealed record OffboardingPlan
{
    public required OffboardingRequest Request { get; init; }

    public required string SourceDn { get; init; }

    public string SamAccountName { get; init; } = "";

    public string SourceOu { get; init; } = "";

    public string TargetOu { get; init; } = "";

    public string NewDn { get; init; } = "";

    public int AccountControlBefore { get; init; }

    public int AccountControlAfter { get; init; }

    public string DescriptionBefore { get; init; } = "";

    public string DescriptionAfter { get; init; } = "";

    /// <summary>Every group the account had (memberOf), saved in the record.</summary>
    public IReadOnlyList<string> GroupsBefore { get; init; } = [];

    public IReadOnlyList<string> GroupsToRemove { get; init; } = [];

    public IReadOnlyList<PlanIssue> Issues { get; init; } = [];

    public bool CanExecute => Issues.All(i => i.Severity != IssueSeverity.Error);
}

public static class OffboardingPlanner
{
    /// <param name="user">The account as read right now.</param>
    /// <param name="groups">The groups in its <c>memberOf</c>, read from the directory (to know which are protected).</param>
    public static OffboardingPlan Plan(DirectoryObject user, IReadOnlyList<DirectoryObject> groups, OffboardingRequest request, PlanningContext ctx)
    {
        var t = ctx.T;
        var s = ctx.Settings;
        var issues = new IssueList();
        var sam = user.SamAccountName ?? "";
        var plan = new OffboardingPlan { Request = request, SourceDn = user.Dn, SamAccountName = sam };

        if (!user.IsUser)
        {
            issues.Error("NOT_A_USER", t.T("{0} is not a user account.", user.Dn));
        }

        if (ProtectedObjects.IsProtectedAccount(user))
        {
            issues.Error("PROTECTED_ACCOUNT", t.T("Protected account (adminCount or built-in), Deckside never changes it: {0}", user.Dn));
        }

        if (!ctx.Scope.IsConfigured(ScopeArea.Users) || !ctx.Scope.IsConfigured(ScopeArea.Disabled))
        {
            issues.Error("OU_NOT_CONFIGURED", t.T("The master OU, the users OU and the disabled OU must be configured."));
        }
        else if (ctx.Scope.Contains(ScopeArea.Disabled, user.Dn))
        {
            issues.Error("ALREADY_OFFBOARDED", t.T("The account is already in the disabled OU."));
        }
        else if (!ctx.Scope.Contains(ScopeArea.Users, user.Dn))
        {
            issues.Error("OUTSIDE_SCOPE", t.T("The account is outside the users OU, it can be read but not changed: {0}", user.Dn));
        }

        if (string.IsNullOrWhiteSpace(request.Reason))
        {
            issues.Error("REASON_REQUIRED", t.T("The reason is required."));
        }

        if (s.RequireTicket && string.IsNullOrWhiteSpace(request.Ticket))
        {
            issues.Error("TICKET_REQUIRED", t.T("The ticket is required."));
        }

        if (issues.HasErrors)
        {
            return plan with { Issues = issues.Items };
        }

        var uacBefore = user.GetInt32("userAccountControl") ?? AccountControlValues.UserEnabled;
        var uacAfter = uacBefore | (int)UserAccountControl.AccountDisable;
        if (uacBefore == uacAfter)
        {
            issues.Warning("ALREADY_DISABLED", t.T("The account was already disabled; it is only moved."));
        }

        var descriptionBefore = user.Get("description") ?? "";
        var today = ctx.Clock.UtcNow.ToLocalTime().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var descriptionAfter = NameNormalizer.Clean(PatternTemplate.Render(s.Offboarding.DescriptionPattern, token => token switch
        {
            "date" => today,
            "ticket" => NameNormalizer.Clean(request.Ticket),
            "reason" => NameNormalizer.Clean(request.Reason),
            "operator" => request.Operator,
            "previous" => descriptionBefore,
            _ => null,
        }));
        if (descriptionAfter.Length > 1024)
        {
            descriptionAfter = descriptionAfter[..1024];
        }

        var rdn = DistinguishedName.FirstRdn(user.Dn);
        var newDn = DistinguishedName.Child(rdn.Type, rdn.Value, s.DisabledOU);
        if (ctx.Checker.IsDnTaken(newDn))
        {
            issues.Error("DN_TAKEN", t.T("An object already exists with the DN {0}", newDn));
        }

        // Groups: protected ones are never touched; without a checkbox only managed groups are removed.
        var groupsBefore = user.GetAll("memberOf").Select(DistinguishedName.Normalize).Order(StringComparer.OrdinalIgnoreCase).ToList();
        var toRemove = new List<string>();
        if (request.RemoveManagedGroups || request.RemoveAllGroups)
        {
            foreach (var dn in groupsBefore)
            {
                if (!ctx.Scope.IsInsideMaster(dn))
                {
                    issues.Warning("GROUP_OUTSIDE_MASTER_OU", t.T("The group is outside the master OU, Deckside does not change it: {0}", dn));
                    continue;
                }

                var group = groups.FirstOrDefault(g => DistinguishedName.AreEqual(g.Dn, dn));
                if (group is not null && ProtectedObjects.IsProtectedGroup(group, s.ExtraProtectedGroups))
                {
                    issues.Warning("PROTECTED_GROUP_KEPT", t.T("Protected group, it is not removed: {0}", dn));
                    continue;
                }

                if (group is null)
                {
                    issues.Warning("GROUP_NOT_READ", t.T("The group could not be read, it is not removed: {0}", dn));
                    continue;
                }

                if (request.RemoveAllGroups || ctx.Profiles.IsManaged(dn))
                {
                    toRemove.Add(dn);
                }
            }
        }

        return plan with
        {
            SourceOu = user.ParentDn,
            TargetOu = DistinguishedName.Normalize(s.DisabledOU),
            NewDn = newDn,
            AccountControlBefore = uacBefore,
            AccountControlAfter = uacAfter,
            DescriptionBefore = descriptionBefore,
            DescriptionAfter = descriptionAfter,
            GroupsBefore = groupsBefore,
            GroupsToRemove = toRemove,
            Issues = issues.Items,
        };
    }
}
