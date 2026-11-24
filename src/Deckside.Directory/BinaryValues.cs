using System.Globalization;
using System.Text;

namespace Deckside.Directory;

/// <summary>Converts the binary attributes Deckside reads into the text form used by <c>DirectoryObject</c>.</summary>
public static class BinaryValues
{
    public static readonly IReadOnlyList<string> BinaryAttributes = ["objectSid", "objectGUID", "tokenGroups", "msLAPS-EncryptedPassword"];

    public static bool IsBinary(string attribute) => BinaryAttributes.Contains(attribute, StringComparer.OrdinalIgnoreCase);

    /// <summary>Binary SID to <c>S-1-5-21-...</c> (works on every OS, unlike SecurityIdentifier).</summary>
    public static string SidToString(byte[] sid)
    {
        if (sid.Length < 8 || sid.Length != 8 + (sid[1] * 4))
        {
            throw new FormatException("Invalid SID length.");
        }

        long authority = 0;
        for (var i = 2; i < 8; i++)
        {
            authority = (authority << 8) | sid[i];
        }

        var sb = new StringBuilder("S-").Append(sid[0].ToString(CultureInfo.InvariantCulture)).Append('-').Append(authority.ToString(CultureInfo.InvariantCulture));
        for (var k = 0; k < sid[1]; k++)
        {
            sb.Append('-').Append(BitConverter.ToUInt32(sid, 8 + (k * 4)).ToString(CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>GUID text, SID text, or base64 for other binary values (the encrypted LAPS blob).</summary>
    public static string ToText(string attribute, byte[] value) =>
        string.Equals(attribute, "objectGUID", StringComparison.OrdinalIgnoreCase) ? new Guid(value).ToString()
        : string.Equals(attribute, "objectSid", StringComparison.OrdinalIgnoreCase) || string.Equals(attribute, "tokenGroups", StringComparison.OrdinalIgnoreCase) ? SidToString(value)
        : Convert.ToBase64String(value);
}
