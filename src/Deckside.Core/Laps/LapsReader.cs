using System.Globalization;
using System.Text.Json;
using Deckside.Core.Directory;

namespace Deckside.Core.Laps;

/// <summary>Where the password came from (English codes; the window translates them).</summary>
public static class LapsSources
{
    public const string WindowsLaps = "WINDOWS_LAPS";
    public const string WindowsLapsEncrypted = "WINDOWS_LAPS_ENCRYPTED";
    public const string LegacyLaps = "LEGACY_LAPS";
}

/// <summary>Result of reading the LAPS attributes of one computer.</summary>
public sealed record LapsResult
{
    /// <summary>OK, NO_PASSWORD (no LAPS or no permission to read it) or DECRYPT_FAILED.</summary>
    public required string Status { get; init; }

    public string Source { get; init; } = "";

    /// <summary>Managed local account. Empty for legacy LAPS (it does not store it).</summary>
    public string Account { get; init; } = "";

    /// <summary>The password. Never logged, never written to a record (security rule 6 applies to it too).</summary>
    public string Password { get; init; } = "";

    public DateTimeOffset? UpdatedUtc { get; init; }

    public DateTimeOffset? ExpiresUtc { get; init; }

    /// <summary>Detail of a decryption error (Win32 / NTE code), never the password.</summary>
    public string Detail { get; init; } = "";

    public bool Found => Status == "OK";
}

/// <summary>
/// Reads the LAPS password the same way the standard tools do (<c>Get-LapsADPassword</c>, the LAPS UI):
/// Windows LAPS encrypted first, then Windows LAPS in clear text, then legacy Microsoft LAPS. AD only returns
/// these attributes to accounts allowed to read them, so "no password" can also mean "no permission".
/// </summary>
public static class LapsReader
{
    public const string LegacyPassword = "ms-Mcs-AdmPwd";
    public const string LegacyExpiration = "ms-Mcs-AdmPwdExpirationTime";
    public const string Password = "msLAPS-Password";
    public const string EncryptedPassword = "msLAPS-EncryptedPassword";
    public const string Expiration = "msLAPS-PasswordExpirationTime";

    /// <summary>Attributes to request for a computer (unknown ones are simply not returned by AD).</summary>
    public static readonly IReadOnlyList<string> Attributes =
    [
        "cn", "sAMAccountName", "dNSHostName", "operatingSystem", "objectClass",
        LegacyPassword, LegacyExpiration, Password, EncryptedPassword, Expiration,
    ];

    /// <summary>Encrypted blob header: timestamp high (4), timestamp low (4), buffer size (4), flags (4).</summary>
    public const int EncryptedHeaderLength = 16;

    /// <param name="computer">The computer with <see cref="Attributes"/>; <c>msLAPS-EncryptedPassword</c> comes as base64.</param>
    /// <param name="decrypt">Decrypts the DPAPI-NG blob (Windows only) and returns the UTF-16 JSON; throws on failure.</param>
    public static LapsResult Read(DirectoryObject computer, Func<byte[], string>? decrypt)
    {
        var expires = AdTime.FromFileTime(computer.Get(Expiration));
        string? decryptError = null;

        var encrypted = computer.Get(EncryptedPassword);
        if (encrypted is not null && decrypt is not null)
        {
            try
            {
                var json = decrypt(EncryptedBuffer(Convert.FromBase64String(encrypted)));
                return FromJson(json, LapsSources.WindowsLapsEncrypted, expires);
            }
            catch (Exception ex) when (ex is FormatException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                decryptError = ex.Message;
            }
        }
        else if (encrypted is not null)
        {
            decryptError = "NOT_SUPPORTED";
        }

        var clear = computer.Get(Password);
        if (clear is not null)
        {
            try
            {
                return FromJson(clear, LapsSources.WindowsLaps, expires);
            }
            catch (Exception ex) when (ex is JsonException or FormatException)
            {
                return new LapsResult { Status = "INVALID_DATA", Source = LapsSources.WindowsLaps, Detail = ex.Message, ExpiresUtc = expires };
            }
        }

        var legacy = computer.Get(LegacyPassword);
        if (legacy is not null && decryptError is null)
        {
            return new LapsResult
            {
                Status = "OK",
                Source = LapsSources.LegacyLaps,
                Password = legacy,
                ExpiresUtc = AdTime.FromFileTime(computer.Get(LegacyExpiration)),
            };
        }

        return decryptError is not null
            ? new LapsResult { Status = "DECRYPT_FAILED", Source = LapsSources.WindowsLapsEncrypted, Detail = decryptError, ExpiresUtc = expires }
            : new LapsResult { Status = "NO_PASSWORD", ExpiresUtc = expires ?? AdTime.FromFileTime(computer.Get(LegacyExpiration)) };
    }

    /// <summary>The DPAPI-NG part of <c>msLAPS-EncryptedPassword</c> (after the 16-byte header).</summary>
    public static byte[] EncryptedBuffer(byte[] blob)
    {
        if (blob.Length <= EncryptedHeaderLength)
        {
            throw new FormatException("The encrypted LAPS value is too short.");
        }

        var size = BitConverter.ToUInt32(blob, 8);
        if (size == 0 || size > blob.Length - EncryptedHeaderLength)
        {
            throw new FormatException("The encrypted LAPS value has an invalid size.");
        }

        return blob[EncryptedHeaderLength..(EncryptedHeaderLength + (int)size)];
    }

    /// <summary>Windows LAPS JSON: <c>{"n":"account","t":"&lt;FILETIME hex&gt;","p":"password"}</c>.</summary>
    public static LapsResult FromJson(string json, string source, DateTimeOffset? expires)
    {
        using var doc = JsonDocument.Parse(json.TrimEnd('\0'));
        var root = doc.RootElement;
        var password = root.TryGetProperty("p", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
        if (string.IsNullOrEmpty(password))
        {
            throw new FormatException("The LAPS value has no password.");
        }

        DateTimeOffset? updated = null;
        if (root.TryGetProperty("t", out var t) && t.ValueKind == JsonValueKind.String &&
            long.TryParse(t.GetString(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var fileTime))
        {
            updated = AdTime.FromFileTime(fileTime);
        }

        return new LapsResult
        {
            Status = "OK",
            Source = source,
            Account = root.TryGetProperty("n", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "",
            Password = password,
            UpdatedUtc = updated,
            ExpiresUtc = expires,
        };
    }

    /// <summary>
    /// Normalizes what the operator typed: <c>pc01</c>, <c>PC01$</c> or <c>pc01.example.local</c> → <c>PC01</c>.
    /// Returns empty when it is not a valid computer name.
    /// </summary>
    public static string NormalizeComputerName(string? typed)
    {
        var name = (typed ?? "").Trim();
        var dot = name.IndexOf('.', StringComparison.Ordinal);
        if (dot > 0)
        {
            name = name[..dot];
        }

        name = name.TrimEnd('$').ToUpperInvariant();
        return Naming.AccountNames.ComputerNameProblems(name).Count == 0 ? name : "";
    }
}
