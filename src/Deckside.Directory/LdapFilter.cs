using System.Globalization;
using System.Text;

namespace Deckside.Directory;

/// <summary>Builds LDAP filters with escaped values (RFC 4515), so a typed name cannot change the filter.</summary>
public static class LdapFilter
{
    public static string Escape(string value)
    {
        var sb = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            if (c is '*' or '(' or ')' or '\\' or '\0')
            {
                sb.Append('\\').Append(((int)c).ToString("x2", CultureInfo.InvariantCulture));
            }
            else
            {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    public static string Equal(string attribute, string value) => "(" + attribute + "=" + Escape(value) + ")";

    /// <summary><c>(attribute=prefix*)</c>: used to find the existing names of a sequence.</summary>
    public static string StartsWith(string attribute, string prefix) => "(" + attribute + "=" + Escape(prefix) + "*)";

    public static string And(params string[] filters) => "(&" + string.Concat(filters) + ")";

    public static string Or(params string[] filters) => "(|" + string.Concat(filters) + ")";

    public const string Users = "(&(objectCategory=person)(objectClass=user))";

    public const string Computers = "(objectClass=computer)";

    public const string Groups = "(objectClass=group)";
}
