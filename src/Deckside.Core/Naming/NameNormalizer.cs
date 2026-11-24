using System.Globalization;
using System.Text;

namespace Deckside.Core.Naming;

/// <summary>Turns person names into the ASCII used by e-mail addresses and account names.</summary>
public static class NameNormalizer
{
    /// <summary>Collapses spaces and trims (the form kept in <c>givenName</c>, <c>sn</c>, CN, displayName).</summary>
    public static string Clean(string? value) =>
        string.Join(' ', (value ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// Lower-case ASCII letters and digits only: accents removed (<c>á</c> → <c>a</c>, <c>ñ</c> → <c>n</c>,
    /// <c>ü</c> → <c>u</c>), a few letters without decomposition mapped (<c>ß</c>, <c>æ</c>, <c>ø</c>, <c>ł</c>...),
    /// and spaces, apostrophes, hyphens and anything else dropped (<c>De la Peña</c> → <c>delapena</c>).
    /// </summary>
    public static string ToAsciiLower(string? value)
    {
        var sb = new StringBuilder();
        foreach (var c in Clean(value).Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var lower = char.ToLowerInvariant(c);
            switch (lower)
            {
                case 'ß': sb.Append("ss"); continue;
                case 'æ': sb.Append("ae"); continue;
                case 'œ': sb.Append("oe"); continue;
                case 'ø': sb.Append('o'); continue;
                case 'ł': sb.Append('l'); continue;
                case 'đ': sb.Append('d'); continue;
                case 'ð': sb.Append('d'); continue;
                case 'þ': sb.Append("th"); continue;
                case 'ı': sb.Append('i'); continue;
            }

            if (char.IsAsciiLetterLower(lower) || char.IsAsciiDigit(lower))
            {
                sb.Append(lower);
            }
        }

        return sb.ToString();
    }

    /// <summary>First letter of <see cref="ToAsciiLower"/>, or empty.</summary>
    public static string Initial(string? value)
    {
        var ascii = ToAsciiLower(value);
        return ascii.Length > 0 ? ascii[..1] : string.Empty;
    }

    /// <summary>First word of a multi-word value (<c>Juan Carlos</c> → <c>Juan</c>).</summary>
    public static string FirstWord(string? value)
    {
        var clean = Clean(value);
        var space = clean.IndexOf(' ', StringComparison.Ordinal);
        return space < 0 ? clean : clean[..space];
    }

    /// <summary>True for the characters accepted in a person-name field: letters, marks, spaces, <c>' - .</c></summary>
    public static bool IsValidPersonName(string value) =>
        value.All(c => char.IsLetter(c) || CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark ||
                       c is ' ' or '\'' or '-' or '.' or '’');
}
