using System.Globalization;

namespace Deckside.Core.Naming;

/// <summary>The e-mail address chosen for a new user and which candidate it came from.</summary>
public sealed record MailProposal(string Address, string LocalPart, int CandidateIndex, string Pattern, IReadOnlyList<string> TakenCandidates)
{
    public bool UsedFallback => CandidateIndex > 0;
}

/// <summary>Builds e-mail, UPN, CN, displayName and <c>sn</c> from the naming settings.</summary>
public static class UserNameBuilder
{
    /// <summary>Tokens of the e-mail patterns: the person tokens plus <c>{employeeId}</c> and <c>{n}</c> (2, 3...).</summary>
    public static readonly IReadOnlyList<string> MailTokens = [.. PersonName.AsciiTokens, "employeeId", "n"];

    public static readonly IReadOnlyList<string> UpnTokens = ["sam", "mailLocal", "mailDomain", "upnSuffix"];

    public static readonly IReadOnlyList<string> CnTokens = [.. PersonName.DisplayTokens, "EmployeeId"];

    public const int MaxNumberedCandidate = 99;

    /// <summary>
    /// First address that is not taken: the main pattern, then each fallback in order (a fallback with
    /// <c>{n}</c> tries 2..99). Candidates equal to an earlier one are skipped (e.g. <c>{surname1}{s2}</c>
    /// when there is no second surname).
    /// </summary>
    public static MailProposal BuildMail(PersonName name, string? employeeId, string mainPattern, IReadOnlyList<string> fallbacks, string mailDomain, Func<string, bool> isTaken)
    {
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var taken = new List<string>();
        var patterns = new List<string> { mainPattern };
        patterns.AddRange(fallbacks);
        var index = 0;
        foreach (var pattern in patterns)
        {
            var numbered = PatternTemplate.Tokens(pattern).Contains("n", StringComparer.Ordinal);
            var range = numbered ? Enumerable.Range(2, MaxNumberedCandidate - 1) : [0];
            foreach (var n in range)
            {
                var local = CleanLocalPart(PatternTemplate.Render(pattern, t => t switch
                {
                    "n" => n.ToString(CultureInfo.InvariantCulture),
                    "employeeId" => NameNormalizer.ToAsciiLower(employeeId),
                    _ => name.ResolveAscii(t),
                }));
                if (!AccountNames.IsValidMailLocalPart(local))
                {
                    throw new NamingException("MAIL_INVALID", local);
                }

                var address = local + "@" + mailDomain.ToLowerInvariant();
                if (!tried.Add(address))
                {
                    continue;
                }

                if (!isTaken(address))
                {
                    return new MailProposal(address, local, index, pattern, taken);
                }

                taken.Add(address);
            }

            index++;
        }

        throw new NamingException("MAIL_EXHAUSTED", string.Join(", ", taken));
    }

    public static string BuildUpn(string pattern, string sam, string mailLocal, string mailDomain, string upnSuffix) =>
        PatternTemplate.Render(pattern, t => t switch
        {
            "sam" => sam,
            "mailLocal" => mailLocal,
            "mailDomain" => mailDomain.ToLowerInvariant(),
            "upnSuffix" => upnSuffix.ToLowerInvariant(),
            _ => null,
        });

    /// <summary>CN, displayName and sn patterns: the name as typed, with empty parts collapsed.</summary>
    public static string BuildDisplay(string pattern, PersonName name, string? employeeId) =>
        NameNormalizer.Clean(PatternTemplate.Render(pattern, t => t == "EmployeeId" ? (employeeId ?? string.Empty) : name.ResolveDisplay(t)))
            .Trim(',', ' ', '-');

    /// <summary>Removes the separators left by empty tokens: <c>juan..perez.</c> → <c>juan.perez</c>.</summary>
    internal static string CleanLocalPart(string value)
    {
        var sb = new System.Text.StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            var separator = c is '.' or '_' or '-';
            if (separator && (sb.Length == 0 || sb[^1] is '.' or '_' or '-'))
            {
                continue;
            }

            sb.Append(c);
        }

        while (sb.Length > 0 && sb[^1] is '.' or '_' or '-')
        {
            sb.Length--;
        }

        return sb.ToString();
    }
}
