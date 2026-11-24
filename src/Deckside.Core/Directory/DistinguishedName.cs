using System.Globalization;
using System.Text;

namespace Deckside.Core.Directory;

/// <summary>One component of a DN, e.g. <c>OU=Sales</c>. The value is stored unescaped.</summary>
public readonly record struct Rdn(string Type, string Value)
{
    public bool Matches(Rdn other) =>
        string.Equals(Type, other.Type, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Value, other.Value, StringComparison.OrdinalIgnoreCase);

    public override string ToString() => Type + "=" + DistinguishedName.EscapeValue(Value);
}

/// <summary>
/// Distinguished-name helpers (RFC 4514). Comparisons ignore case and the way values were escaped, as
/// Active Directory does, so <c>CN=Perez\, Juan</c> and <c>cn=PEREZ\2C JUAN</c> are the same object.
/// Multi-valued RDNs (<c>+</c>) are not used by AD for OUs, users or computers and are rejected.
/// </summary>
public static class DistinguishedName
{
    public static IReadOnlyList<Rdn> Parse(string dn)
    {
        ArgumentNullException.ThrowIfNull(dn);
        var result = new List<Rdn>();
        var i = 0;
        while (i < dn.Length)
        {
            var type = new StringBuilder();
            while (i < dn.Length && dn[i] != '=')
            {
                type.Append(dn[i++]);
            }

            if (i >= dn.Length)
            {
                throw new FormatException("Invalid DN (missing '='): " + dn);
            }

            i++; // '='
            while (i < dn.Length && dn[i] == ' ')
            {
                i++; // spaces before the value are not part of it (an escaped one is)
            }

            var value = new StringBuilder();
            var lastSignificant = 0; // length of value without unescaped trailing spaces
            while (i < dn.Length && dn[i] != ',')
            {
                var c = dn[i];
                if (c == '+')
                {
                    throw new FormatException("Multi-valued RDNs are not supported: " + dn);
                }

                if (c == '\\')
                {
                    if (i + 1 >= dn.Length)
                    {
                        throw new FormatException("Invalid DN (dangling escape): " + dn);
                    }

                    if (i + 2 < dn.Length && IsHex(dn[i + 1]) && IsHex(dn[i + 2]))
                    {
                        // \XX hex pairs: collect consecutive ones as UTF-8 bytes.
                        var bytes = new List<byte>();
                        while (i + 2 < dn.Length && dn[i] == '\\' && IsHex(dn[i + 1]) && IsHex(dn[i + 2]))
                        {
                            bytes.Add(byte.Parse(dn.AsSpan(i + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 3;
                        }

                        value.Append(Encoding.UTF8.GetString([.. bytes]));
                    }
                    else
                    {
                        value.Append(dn[i + 1]);
                        i += 2;
                    }

                    lastSignificant = value.Length;
                    continue;
                }

                value.Append(c);
                if (c != ' ')
                {
                    lastSignificant = value.Length;
                }

                i++;
            }

            var t = type.ToString().Trim();
            var v = value.ToString(0, lastSignificant);
            if (t.Length == 0 || v.Length == 0)
            {
                throw new FormatException("Invalid DN (empty type or value): " + dn);
            }

            result.Add(new Rdn(t, v));
            if (i < dn.Length)
            {
                i++; // ','
                if (i == dn.Length)
                {
                    throw new FormatException("Invalid DN (trailing comma): " + dn);
                }
            }
        }

        if (result.Count == 0)
        {
            throw new FormatException("Empty DN.");
        }

        return result;
    }

    public static bool TryParse(string? dn, out IReadOnlyList<Rdn> rdns)
    {
        try
        {
            rdns = Parse(dn ?? string.Empty);
            return true;
        }
        catch (FormatException)
        {
            rdns = [];
            return false;
        }
    }

    public static bool IsValid(string? dn) => TryParse(dn, out _);

    /// <summary>Canonical form used to compare and as dictionary key (escaped consistently, case kept).</summary>
    public static string Normalize(string dn) => Join(Parse(dn));

    public static string Join(IEnumerable<Rdn> rdns) => string.Join(",", rdns.Select(r => r.ToString()));

    public static bool AreEqual(string a, string b)
    {
        var x = Parse(a);
        var y = Parse(b);
        return x.Count == y.Count && x.Zip(y).All(p => p.First.Matches(p.Second));
    }

    /// <summary>True when <paramref name="dn"/> is strictly below <paramref name="container"/> (at any depth).</summary>
    public static bool IsDescendantOf(string dn, string container)
    {
        var x = Parse(dn);
        var y = Parse(container);
        if (x.Count <= y.Count)
        {
            return false;
        }

        var offset = x.Count - y.Count;
        for (var k = 0; k < y.Count; k++)
        {
            if (!x[offset + k].Matches(y[k]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>True when <paramref name="dn"/> is directly inside <paramref name="container"/> (one level).</summary>
    public static bool IsChildOf(string dn, string container) =>
        Parse(dn).Count == Parse(container).Count + 1 && IsDescendantOf(dn, container);

    public static string Parent(string dn)
    {
        var rdns = Parse(dn);
        if (rdns.Count < 2)
        {
            throw new FormatException("The DN has no parent: " + dn);
        }

        return Join(rdns.Skip(1));
    }

    public static Rdn FirstRdn(string dn) => Parse(dn)[0];

    public static string Child(string type, string value, string parentDn) => new Rdn(type, value) + "," + Normalize(parentDn);

    /// <summary>The DNS domain of the <c>DC=</c> components (<c>DC=example,DC=local</c> → <c>example.local</c>).</summary>
    public static string DomainOf(string dn) =>
        string.Join(".", Parse(dn).Where(r => string.Equals(r.Type, "DC", StringComparison.OrdinalIgnoreCase)).Select(r => r.Value));

    /// <summary>Escapes an attribute value for a DN (RFC 4514 section 2.4).</summary>
    public static string EscapeValue(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            var mustEscape = c is ',' or '+' or '"' or '\\' or '<' or '>' or ';' or '='
                || (i == 0 && (c == '#' || c == ' '))
                || (i == value.Length - 1 && c == ' ');
            if (c < 0x20)
            {
                sb.Append('\\').Append(((int)c).ToString("X2", CultureInfo.InvariantCulture));
                continue;
            }

            if (mustEscape)
            {
                sb.Append('\\');
            }

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static bool IsHex(char c) => char.IsAsciiHexDigit(c);
}
