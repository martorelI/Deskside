using System.Globalization;
using System.Text;
using Deckside.Core.Health;
using Deckside.Core.Localization;
using Deckside.Core.Plans;

namespace Deckside.Core.Text;

/// <summary>
/// Plain-text form of the plans (shown in the confirmation, saved as plan.txt and used by the snapshots).
/// Secrets never appear: the password is a step, not a value.
/// </summary>
public static class PlanText
{
    public static string Render(CreateUserPlan plan, Translator t)
    {
        var sb = new StringBuilder();
        Line(sb, t.T("Create user"));
        Field(sb, t.T("Target OU"), plan.TargetOu);
        Field(sb, "DN", plan.Dn);
        AppendAttributes(sb, t, plan.Attributes);
        AppendGroups(sb, t.T("Groups to add"), plan.GroupsToAdd, '+');
        if (plan.CanExecute)
        {
            Line(sb, t.T("Steps"));
            Line(sb, "  1. " + t.T("Create the account disabled (userAccountControl = 514)."));
            Line(sb, "  2. " + t.T("Set the initial password (encrypted connection; it is never logged)."));
            Line(sb, "  3. " + t.T("User must change the password at next logon (pwdLastSet = 0)."));
            Line(sb, "  4. " + t.T("Enable the account (userAccountControl = 512)."));
            if (plan.GroupsToAdd.Count > 0)
            {
                Line(sb, "  5. " + t.T("Add it to the groups."));
            }
        }

        AppendIssues(sb, t, plan.Issues);
        return sb.ToString();
    }

    public static string Render(CreateComputerPlan plan, Translator t)
    {
        var sb = new StringBuilder();
        Line(sb, t.T("Create computer"));
        Field(sb, t.T("Target OU"), plan.TargetOu);
        Field(sb, "DN", plan.Dn);
        AppendAttributes(sb, t, plan.Attributes);
        AppendGroups(sb, t.T("Groups to add"), plan.GroupsToAdd, '+');
        AppendIssues(sb, t, plan.Issues);
        return sb.ToString();
    }

    public static string Render(OffboardingPlan plan, Translator t)
    {
        var sb = new StringBuilder();
        Line(sb, t.T("Disable and move"));
        Field(sb, t.T("Account"), plan.SamAccountName);
        Field(sb, "DN", plan.SourceDn);
        if (plan.CanExecute)
        {
            Field(sb, t.T("Move to"), plan.NewDn);
            Field(sb, "userAccountControl", Num(plan.AccountControlBefore) + " -> " + Num(plan.AccountControlAfter));
            Field(sb, "description", Quote(plan.DescriptionBefore) + " -> " + Quote(plan.DescriptionAfter));
            AppendGroups(sb, t.T("Groups before"), plan.GroupsBefore, '·');
            AppendGroups(sb, t.T("Groups to remove"), plan.GroupsToRemove, '-');
        }

        AppendIssues(sb, t, plan.Issues);
        return sb.ToString();
    }

    public static string Render(ProfileChangePlan plan, Translator t)
    {
        var sb = new StringBuilder();
        Line(sb, t.T("Assign / change profile"));
        Field(sb, t.T("Account"), plan.SamAccountName);
        Field(sb, "DN", plan.ObjectDn);
        Field(sb, t.T("Profiles"), string.Join(", ", plan.Profiles));
        if (plan.CanExecute)
        {
            AppendGroups(sb, t.T("Groups to add"), plan.Difference.ToAdd, '+');
            AppendGroups(sb, t.T("Groups to remove"), plan.Difference.ToRemove, '-');
            AppendGroups(sb, t.T("Groups kept"), plan.Difference.Keep, '=');
            AppendGroups(sb, t.T("Other groups (not managed, untouched)"), plan.Difference.Untouched, '·');
        }

        AppendIssues(sb, t, plan.Issues);
        return sb.ToString();
    }

    public static string Render(HealthReport report, Translator t)
    {
        var sb = new StringBuilder();
        Line(sb, t.T("Health report") + " " + report.GeneratedUtc.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture));
        foreach (var section in new[] { report.Users, report.Computers })
        {
            Line(sb, "");
            Line(sb, (section.Kind == HealthObjectKind.User ? t.T("Users") : t.T("Computers")) + ": " + section.Ou);
            Field(sb, t.T("Objects"), Num(section.Rows.Count));
            Field(sb, t.T("With problems"), Num(section.WithProblems));
            foreach (var code in section.Codes)
            {
                Line(sb, "    " + code.PadRight(24) + Num(section.Count(code)));
            }

            foreach (var row in section.Rows)
            {
                Line(sb, "  - " + row.SamAccountName + " | " + (row.Findings.Count == 0 ? "OK" : string.Join(", ", row.Findings)) +
                         " | " + t.T("last logon") + ": " + Date(row.LastLogon) + " | " + t.T("password age") + ": " +
                         (row.PasswordAgeDays is null ? "-" : Num(row.PasswordAgeDays.Value)));
            }
        }

        return sb.ToString();
    }

    private static void AppendAttributes(StringBuilder sb, Translator t, IReadOnlyList<AttributeChange> attributes)
    {
        if (attributes.Count == 0)
        {
            return;
        }

        Line(sb, t.T("Attributes"));
        foreach (var a in attributes)
        {
            Line(sb, "  " + a.Name + " = " + (a.IsSecret ? Security.SecretMasker.Mask : string.Join("; ", a.After)));
        }
    }

    /// <param name="marker"><c>+</c> added, <c>-</c> removed, <c>=</c> kept, <c>·</c> listed only.</param>
    private static void AppendGroups(StringBuilder sb, string title, IReadOnlyList<string> groups, char marker)
    {
        Line(sb, title + " (" + Num(groups.Count) + ")");
        foreach (var g in groups)
        {
            Line(sb, "  " + marker + " " + g);
        }
    }

    private static void AppendIssues(StringBuilder sb, Translator t, IReadOnlyList<PlanIssue> issues)
    {
        foreach (var i in issues)
        {
            var label = i.Severity switch
            {
                IssueSeverity.Error => t.T("ERROR"),
                IssueSeverity.Warning => t.T("WARNING"),
                _ => t.T("NOTE"),
            };
            Line(sb, label + " [" + i.Code + "] " + i.Message);
        }
    }

    private static void Field(StringBuilder sb, string name, string value) => Line(sb, name + ": " + value);

    private static void Line(StringBuilder sb, string text) => sb.Append(text).Append('\n');

    private static string Num(int n) => n.ToString(CultureInfo.InvariantCulture);

    private static string Quote(string s) => "\"" + s + "\"";

    private static string Date(DateTimeOffset? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "-";
}
