using System.Globalization;
using Deckside.Core.Directory;
using Deckside.Core.Naming;
using Deckside.Core.Profiles;

namespace Deckside.Core.Plans;

public sealed record CreateComputerRequest
{
    /// <summary>Name typed by the operator; empty = next sequential of the computer pattern.</summary>
    public string Name { get; init; } = "";

    public string Description { get; init; } = "";

    public string Ticket { get; init; } = "";

    /// <summary>Groups chosen in the form (DNs). Null = the default computer groups of the settings.</summary>
    public IReadOnlyList<string>? Groups { get; init; }

    public IReadOnlyList<string> Profiles { get; init; } = [];

    public string? TargetOu { get; init; }
}

/// <summary>Exactly what "Computers: create" will write (a pre-staged computer account).</summary>
public sealed record CreateComputerPlan
{
    public required CreateComputerRequest Request { get; init; }

    public string TargetOu { get; init; } = "";

    public string Name { get; init; } = "";

    public string SamAccountName { get; init; } = "";

    public string Dn { get; init; } = "";

    public SequentialProposal? Sequence { get; init; }

    public IReadOnlyList<AttributeChange> Attributes { get; init; } = [];

    public IReadOnlyList<string> GroupsToAdd { get; init; } = [];

    public IReadOnlyList<PlanIssue> Issues { get; init; } = [];

    public bool CanExecute => Issues.All(i => i.Severity != IssueSeverity.Error);
}

public static class CreateComputerPlanner
{
    public static CreateComputerPlan Plan(CreateComputerRequest request, IEnumerable<ExistingName> existingSequence, PlanningContext ctx)
    {
        var t = ctx.T;
        var s = ctx.Settings;
        var issues = new IssueList();
        var targetOu = request.TargetOu ?? s.ComputersOU;
        if (!ctx.Scope.IsConfigured(ScopeArea.Computers))
        {
            issues.Error("OU_NOT_CONFIGURED", t.T("The computers OU is not configured (or it is outside the master OU)."));
        }
        else if (!ctx.Scope.IsValidTarget(ScopeArea.Computers, targetOu))
        {
            issues.Error("OUTSIDE_SCOPE", t.T("The target OU is outside the computers OU: {0}", targetOu));
        }

        if (!AccountNames.IsValidDnsDomain(s.EffectiveDomain))
        {
            issues.Error("DOMAIN_NOT_CONFIGURED", t.T("The domain (or the e-mail domain / UPN suffix) is not configured."));
        }

        if (s.RequireTicket && string.IsNullOrWhiteSpace(request.Ticket))
        {
            issues.Error("TICKET_REQUIRED", t.T("The ticket is required."));
        }

        if (issues.HasErrors)
        {
            return new CreateComputerPlan { Request = request, TargetOu = targetOu, Issues = issues.Items };
        }

        string name;
        SequentialProposal? sequence = null;
        bool IsNameTaken(string candidate) =>
            ctx.Checker.IsSamAccountNameTaken(candidate + "$") || ctx.Checker.IsCnTaken(candidate) ||
            ctx.Checker.IsDnsHostNameTaken(candidate.ToLowerInvariant() + "." + s.EffectiveDomain.ToLowerInvariant());

        if (request.Name.Trim().Length == 0)
        {
            try
            {
                // Computer accounts end with '$': strip it so the pattern matches.
                var stripped = existingSequence.Select(e => e with { Name = e.Name.TrimEnd('$') });
                sequence = Sequential.Next(SequencePattern.Parse(s.Naming.ComputerPattern), s.Naming.ComputerFirstNumber, stripped, IsNameTaken);
                name = sequence.Name.ToUpperInvariant();
                if (sequence.Previous is not null)
                {
                    issues.Info("SEQUENCE_PREVIOUS", t.T("The last number was {0} ({1}).", sequence.Previous.Name, sequence.Previous.Dn));
                }

                if (sequence.SkippedTaken > 0)
                {
                    issues.Warning("SEQUENCE_SKIPPED", t.T("{0} numbers were already taken and were skipped.", sequence.SkippedTaken));
                }
            }
            catch (NamingException)
            {
                issues.Error("SEQUENCE_EXHAUSTED", t.T("There are no more numbers in the pattern {0}.", s.Naming.ComputerPattern));
                return new CreateComputerPlan { Request = request, TargetOu = targetOu, Issues = issues.Items };
            }
        }
        else
        {
            name = request.Name.Trim().ToUpperInvariant();
            if (IsNameTaken(name))
            {
                issues.Error("NAME_TAKEN", t.T("The computer name is already used in the domain: {0}", name));
            }
        }

        if (AccountNames.ComputerNameProblems(name).Count > 0)
        {
            issues.Error("COMPUTER_NAME_INVALID", t.T("Invalid computer name (up to 15 letters, digits or '-'): {0}", name));
        }

        var dn = DistinguishedName.Child("CN", name, targetOu);
        var sam = name + "$";
        var groupDns = request.Groups ?? s.DefaultComputerGroups;
        var groups = ctx.CheckGroupsToAdd(groupDns.Concat(ctx.GroupsOfProfiles(request.Profiles, ProfileTarget.Computer, issues)), issues);
        var attributes = new List<AttributeChange>
        {
            AttributeChange.Set("objectClass", "computer"),
            AttributeChange.Set("cn", name),
            AttributeChange.Set("sAMAccountName", sam),
            AttributeChange.Set("userAccountControl", AccountControlValues.PreStagedComputer.ToString(CultureInfo.InvariantCulture)),
        };
        var description = NameNormalizer.Clean(request.Description);
        if (description.Length > 0)
        {
            attributes.Add(AttributeChange.Set("description", description));
        }

        return new CreateComputerPlan
        {
            Request = request,
            TargetOu = DistinguishedName.Normalize(targetOu),
            Name = name,
            SamAccountName = sam,
            Dn = dn,
            Sequence = sequence,
            Attributes = attributes,
            GroupsToAdd = groups,
            Issues = issues.Items,
        };
    }
}
