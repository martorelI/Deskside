using System.Security.Cryptography;

namespace Deckside.Core.Security;

/// <summary>
/// Generates initial passwords with a cryptographic random source. The result always has upper case,
/// lower case, digits and symbols (it meets the AD complexity rule) and avoids characters that are easy to
/// confuse when read aloud or printed (0/O, 1/l/I).
/// </summary>
public static class PasswordGenerator
{
    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnpqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!#$%&*+-=?@";

    public const int MinimumLength = 12;
    public const int MaximumLength = 64;

    public static string Generate(int length)
    {
        if (length is < MinimumLength or > MaximumLength)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        var chars = new char[length];
        string[] sets = [Upper, Lower, Digits, Symbols];
        for (var i = 0; i < sets.Length; i++)
        {
            chars[i] = Pick(sets[i]);
        }

        const string all = Upper + Lower + Digits + Symbols;
        for (var i = sets.Length; i < length; i++)
        {
            chars[i] = Pick(all);
        }

        RandomNumberGenerator.Shuffle(chars.AsSpan());
        return new string(chars);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}

/// <summary>
/// Local check of a typed password against the default AD complexity rule (three of four character classes,
/// does not contain the account name or any part of the display name of 3+ characters) and a minimum length.
/// The domain may be stricter (fine-grained policies, history): the final word is the DC's answer.
/// </summary>
public static class PasswordRules
{
    public static IReadOnlyList<string> Problems(string password, int minimumLength, string samAccountName, string displayName)
    {
        var problems = new List<string>();
        if (password.Length < minimumLength)
        {
            problems.Add("TOO_SHORT");
        }

        var classes = 0;
        classes += password.Any(char.IsUpper) ? 1 : 0;
        classes += password.Any(char.IsLower) ? 1 : 0;
        classes += password.Any(char.IsDigit) ? 1 : 0;
        classes += password.Any(c => !char.IsLetterOrDigit(c)) ? 1 : 0;
        if (classes < 3)
        {
            problems.Add("NOT_COMPLEX");
        }

        if (samAccountName.Length >= 3 && password.Contains(samAccountName, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add("CONTAINS_ACCOUNT");
        }

        var parts = displayName.Split([' ', ',', '.', '-', '_', '#', '\t'], StringSplitOptions.RemoveEmptyEntries);
        if (parts.Any(p => p.Length >= 3 && password.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            problems.Add("CONTAINS_NAME");
        }

        return problems;
    }
}
