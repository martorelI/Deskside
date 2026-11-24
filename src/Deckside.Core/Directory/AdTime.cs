using System.Globalization;

namespace Deckside.Core.Directory;

/// <summary>Conversions of the AD time formats.</summary>
public static class AdTime
{
    /// <summary><c>accountExpires</c> uses 0 and this value for "never".</summary>
    public const long Never = long.MaxValue;

    /// <summary>
    /// FILETIME (100 ns intervals since 1601-01-01 UTC) as used by <c>pwdLastSet</c>, <c>lastLogonTimestamp</c>,
    /// <c>lastLogon</c>, <c>lockoutTime</c>, <c>badPasswordTime</c>, <c>accountExpires</c>,
    /// <c>msDS-UserPasswordExpiryTimeComputed</c>. Returns null for 0, "never" and unparseable values.
    /// </summary>
    public static DateTimeOffset? FromFileTime(long? value)
    {
        if (value is null or <= 0 or Never)
        {
            return null;
        }

        try
        {
            return DateTimeOffset.FromFileTime(value.Value).ToUniversalTime();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    public static DateTimeOffset? FromFileTime(string? value) =>
        long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? FromFileTime(n) : null;

    public static long ToFileTime(DateTimeOffset value) => value.ToFileTime();

    /// <summary>Generalized time as in <c>whenCreated</c> / <c>whenChanged</c>: <c>20240131235959.0Z</c>.</summary>
    public static DateTimeOffset? FromGeneralizedTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string[] formats = ["yyyyMMddHHmmss.0'Z'", "yyyyMMddHHmmss'Z'", "yyyyMMddHHmmss.f'Z'", "yyyyMMddHHmmss.fff'Z'"];
        return DateTimeOffset.TryParseExact(value, formats, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var result)
            ? result
            : null;
    }

    public static string ToGeneralizedTime(DateTimeOffset value) =>
        value.UtcDateTime.ToString("yyyyMMddHHmmss.0'Z'", CultureInfo.InvariantCulture);
}
