using System.Text;

namespace Deckside.Core.Naming;

/// <summary>
/// Minimal template language of the naming settings: literal text with <c>{token}</c> placeholders, e.g.
/// <c>{given1}.{surname1}</c>. Tokens are case sensitive (<c>{Given}</c> is the name as typed, <c>{given}</c>
/// normalized). <c>{{</c> and <c>}}</c> are literal braces.
/// </summary>
public static class PatternTemplate
{
    public static IReadOnlyList<string> Tokens(string pattern)
    {
        var tokens = new List<string>();
        Walk(pattern, _ => { }, t => tokens.Add(t));
        return tokens;
    }

    /// <summary>Renders the pattern. <paramref name="resolve"/> returns null for an unknown token (FormatException).</summary>
    public static string Render(string pattern, Func<string, string?> resolve)
    {
        var sb = new StringBuilder();
        Walk(pattern, c => sb.Append(c), t => sb.Append(resolve(t) ?? throw new FormatException("Unknown token {" + t + "}")));
        return sb.ToString();
    }

    /// <summary>True when the pattern is well formed and only uses the allowed tokens.</summary>
    public static bool IsValid(string? pattern, IReadOnlyCollection<string> allowedTokens, out string? problem)
    {
        problem = null;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            problem = "EMPTY";
            return false;
        }

        try
        {
            var unknown = Tokens(pattern).FirstOrDefault(t => !allowedTokens.Contains(t, StringComparer.Ordinal));
            if (unknown is not null)
            {
                problem = "UNKNOWN_TOKEN {" + unknown + "}";
                return false;
            }
        }
        catch (FormatException ex)
        {
            problem = ex.Message;
            return false;
        }

        return true;
    }

    private static void Walk(string pattern, Action<char> literal, Action<string> token)
    {
        for (var i = 0; i < pattern.Length; i++)
        {
            var c = pattern[i];
            if (c == '{')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '{')
                {
                    literal('{');
                    i++;
                    continue;
                }

                var end = pattern.IndexOf('}', i + 1);
                if (end < 0)
                {
                    throw new FormatException("Unclosed '{' in pattern: " + pattern);
                }

                var name = pattern[(i + 1)..end];
                if (name.Length == 0 || name.Contains('{', StringComparison.Ordinal))
                {
                    throw new FormatException("Empty or nested token in pattern: " + pattern);
                }

                token(name);
                i = end;
            }
            else if (c == '}')
            {
                if (i + 1 < pattern.Length && pattern[i + 1] == '}')
                {
                    literal('}');
                    i++;
                    continue;
                }

                throw new FormatException("Unexpected '}' in pattern: " + pattern);
            }
            else
            {
                literal(c);
            }
        }
    }
}
