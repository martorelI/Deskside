namespace Deckside.Core.Plans;

public enum IssueSeverity
{
    /// <summary>Shown to the operator; does not change the plan.</summary>
    Info,

    /// <summary>The plan can run, but the operator must read it (it is repeated in the confirmation).</summary>
    Warning,

    /// <summary>The plan cannot run.</summary>
    Error,
}

/// <summary>A finding of a planner. <see cref="Code"/> is an English code (tests and logic compare it), <see cref="Message"/> is translated.</summary>
public sealed record PlanIssue(IssueSeverity Severity, string Code, string Message);

/// <summary>One attribute of an object to create, or one change on an existing object.</summary>
public sealed record AttributeChange(string Name, IReadOnlyList<string> Before, IReadOnlyList<string> After)
{
    public static AttributeChange Set(string name, params string[] values) => new(name, [], values);

    public bool IsSecret => Security.SecretMasker.IsSecretAttribute(Name);
}

/// <summary>Collects the issues of a plan.</summary>
public sealed class IssueList
{
    private readonly List<PlanIssue> items = [];

    public IReadOnlyList<PlanIssue> Items => items;

    public bool HasErrors => items.Any(i => i.Severity == IssueSeverity.Error);

    public void Error(string code, string message) => items.Add(new PlanIssue(IssueSeverity.Error, code, message));

    public void Warning(string code, string message) => items.Add(new PlanIssue(IssueSeverity.Warning, code, message));

    public void Info(string code, string message) => items.Add(new PlanIssue(IssueSeverity.Info, code, message));
}

/// <summary>
/// Uniqueness checks against the whole domain (security rule 7). The Operations layer implements it with
/// live directory searches; the planners call it while building the plan and the operation calls it again
/// right before writing.
/// </summary>
public interface IUniquenessChecker
{
    bool IsSamAccountNameTaken(string samAccountName);

    bool IsUpnTaken(string userPrincipalName);

    /// <summary>The address is in <c>mail</c> or in <c>proxyAddresses</c> (any <c>smtp:</c> prefix) of any object.</summary>
    bool IsMailTaken(string address);

    /// <summary>Some object in the domain already has this CN.</summary>
    bool IsCnTaken(string cn);

    /// <summary>An object with this exact DN exists.</summary>
    bool IsDnTaken(string dn);

    bool IsDnsHostNameTaken(string dnsHostName);
}
