namespace Deckside.Core.Naming;

/// <summary>Validation of account and computer names (problem codes in English).</summary>
public static class AccountNames
{
    public const int MaxUserSamLength = 20;
    public const int MaxComputerNameLength = 15;
    public const int MaxCnLength = 64;
    public const int MaxDisplayNameLength = 256;
    public const int MaxMailLocalPartLength = 64;

    private const string InvalidSamChars = "\"/\\[]:;|=,+*?<>";

    public static IReadOnlyList<string> UserSamProblems(string sam)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(sam))
        {
            problems.Add("EMPTY");
            return problems;
        }

        if (sam.Length > MaxUserSamLength)
        {
            problems.Add("TOO_LONG");
        }

        if (sam.Any(c => InvalidSamChars.Contains(c, StringComparison.Ordinal) || char.IsControl(c)))
        {
            problems.Add("INVALID_CHARACTER");
        }

        if (sam.EndsWith('.') || sam.Trim().Length != sam.Length || sam.All(c => c is '.' or ' '))
        {
            problems.Add("INVALID_FORM");
        }

        return problems;
    }

    /// <summary>NetBIOS computer name (without the trailing <c>$</c>).</summary>
    public static IReadOnlyList<string> ComputerNameProblems(string name)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
        {
            problems.Add("EMPTY");
            return problems;
        }

        if (name.Length > MaxComputerNameLength)
        {
            problems.Add("TOO_LONG");
        }

        if (!name.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
        {
            problems.Add("INVALID_CHARACTER");
        }

        if (name.All(char.IsAsciiDigit) || name.StartsWith('-') || name.EndsWith('-'))
        {
            problems.Add("INVALID_FORM");
        }

        return problems;
    }

    /// <summary>Local part of an e-mail address as Deckside builds it: <c>[a-z0-9]+</c> joined by single <c>. _ -</c>.</summary>
    public static bool IsValidMailLocalPart(string local)
    {
        if (local.Length is 0 or > MaxMailLocalPartLength)
        {
            return false;
        }

        var previousSeparator = true; // no separator at the start
        foreach (var c in local)
        {
            var separator = c is '.' or '_' or '-';
            if (separator && previousSeparator)
            {
                return false;
            }

            if (!separator && !(char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c)))
            {
                return false;
            }

            previousSeparator = separator;
        }

        return !previousSeparator;
    }

    /// <summary>DNS domain such as <c>example.local</c> (labels of letters, digits and inner hyphens).</summary>
    public static bool IsValidDnsDomain(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain) || domain.Length > 253)
        {
            return false;
        }

        var labels = domain.Split('.');
        return labels.Length >= 2 && labels.All(l =>
            l.Length is >= 1 and <= 63 && l.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && l[0] != '-' && l[^1] != '-');
    }
}
