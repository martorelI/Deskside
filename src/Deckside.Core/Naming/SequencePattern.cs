using System.Globalization;

namespace Deckside.Core.Naming;

/// <summary>
/// A sequential account name such as <c>U{seq:5}</c> → <c>U00001</c>, <c>U00002</c>... Exactly one
/// <c>{seq:N}</c> token (N = number of digits, zero padded) between an optional literal prefix and suffix.
/// </summary>
public sealed class SequencePattern
{
    private SequencePattern(string text, string prefix, int digits, string suffix)
    {
        Text = text;
        Prefix = prefix;
        Digits = digits;
        Suffix = suffix;
    }

    public string Text { get; }

    public string Prefix { get; }

    public int Digits { get; }

    public string Suffix { get; }

    public long MaxNumber => (long)Math.Pow(10, Digits) - 1;

    /// <summary>Length of every name the pattern produces.</summary>
    public int Length => Prefix.Length + Digits + Suffix.Length;

    public static SequencePattern Parse(string pattern)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        var tokens = PatternTemplate.Tokens(pattern);
        if (tokens.Count != 1 || !tokens[0].StartsWith("seq:", StringComparison.Ordinal))
        {
            throw new FormatException("The pattern needs exactly one {seq:N} token: " + pattern);
        }

        if (!int.TryParse(tokens[0].AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var digits) || digits is < 1 or > 10)
        {
            throw new FormatException("{seq:N} needs N between 1 and 10: " + pattern);
        }

        var start = pattern.IndexOf('{', StringComparison.Ordinal);
        var end = pattern.IndexOf('}', start);
        var prefix = pattern[..start];
        var suffix = pattern[(end + 1)..];
        if (prefix.Length > 0 && char.IsDigit(prefix[^1]))
        {
            throw new FormatException("The prefix cannot end with a digit (the number would be ambiguous): " + pattern);
        }

        if (suffix.Length > 0 && char.IsDigit(suffix[0]))
        {
            throw new FormatException("The suffix cannot start with a digit: " + pattern);
        }

        return new SequencePattern(pattern, prefix, digits, suffix);
    }

    public static bool TryParse(string? pattern, out SequencePattern? result)
    {
        try
        {
            result = Parse(pattern ?? string.Empty);
            return true;
        }
        catch (FormatException)
        {
            result = null;
            return false;
        }
    }

    public string Format(long number)
    {
        if (number < 0 || number > MaxNumber)
        {
            throw new NamingException("SEQUENCE_EXHAUSTED", Text);
        }

        return Prefix + number.ToString("D" + Digits.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture) + Suffix;
    }

    /// <summary>
    /// Recognizes a name of this sequence (prefix and suffix ignoring case, exactly <see cref="Digits"/> digits).
    /// Names with another number of digits are not part of the sequence.
    /// </summary>
    public bool TryMatch(string name, out long number)
    {
        number = 0;
        if (name.Length != Length ||
            !name.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(Suffix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var digits = name.AsSpan(Prefix.Length, Digits);
        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out number);
    }
}

/// <summary>An existing account found while looking for the highest sequential.</summary>
public sealed record ExistingName(string Name, string Dn);

/// <summary>Proposed next sequential name and the account that had the last number.</summary>
public sealed record SequentialProposal(string Name, long Number, ExistingName? Previous, int SkippedTaken);

public static class Sequential
{
    /// <summary>
    /// Next name after the highest existing number (holes are never reused: a number that was used once could
    /// still exist in mailboxes, files or the recycle bin). <paramref name="isTaken"/> is checked again for each
    /// candidate, so a name taken by an object that does not match the pattern (or a race) is skipped.
    /// </summary>
    public static SequentialProposal Next(SequencePattern pattern, long firstNumber, IEnumerable<ExistingName> existing, Func<string, bool>? isTaken = null)
    {
        ExistingName? previous = null;
        var max = -1L;
        foreach (var item in existing)
        {
            if (pattern.TryMatch(item.Name, out var n) && n > max)
            {
                max = n;
                previous = item;
            }
        }

        var next = Math.Max(max + 1, firstNumber);
        var skipped = 0;
        while (true)
        {
            var name = pattern.Format(next); // throws SEQUENCE_EXHAUSTED past the last number
            if (isTaken is null || !isTaken(name))
            {
                return new SequentialProposal(name, next, previous, skipped);
            }

            skipped++;
            next++;
        }
    }
}
