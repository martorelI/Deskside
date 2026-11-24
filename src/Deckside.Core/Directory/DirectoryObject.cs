namespace Deckside.Core.Directory;

/// <summary>
/// An object read from the directory, as plain strings: the Directory layer converts binary values
/// (<c>objectSid</c> to <c>S-1-5-...</c>, <c>objectGUID</c> to its GUID text) and leaves the rest as AD returns
/// them (FILETIME integers, generalized time). Attribute names ignore case.
/// </summary>
public sealed class DirectoryObject
{
    private readonly Dictionary<string, IReadOnlyList<string>> attributes;

    public DirectoryObject(string dn, IEnumerable<KeyValuePair<string, IReadOnlyList<string>>> attributes)
    {
        Dn = DistinguishedName.Normalize(dn);
        this.attributes = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, values) in attributes)
        {
            this.attributes[name] = [.. values];
        }
    }

    public string Dn { get; }

    public IReadOnlyDictionary<string, IReadOnlyList<string>> Attributes => attributes;

    public string? Get(string name) => attributes.TryGetValue(name, out var v) && v.Count > 0 ? v[0] : null;

    public IReadOnlyList<string> GetAll(string name) => attributes.TryGetValue(name, out var v) ? v : [];

    public long? GetInt64(string name) =>
        long.TryParse(Get(name), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    public int? GetInt32(string name) =>
        int.TryParse(Get(name), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;

    public bool HasClass(string objectClass) =>
        GetAll("objectClass").Contains(objectClass, StringComparer.OrdinalIgnoreCase);

    /// <summary>Computers are also <c>user</c> in AD, so check computer first.</summary>
    public bool IsComputer => HasClass("computer");

    public bool IsUser => HasClass("user") && !IsComputer;

    public bool IsGroup => HasClass("group");

    public string? SamAccountName => Get("sAMAccountName");

    public string? Sid => Get("objectSid");

    public bool HasAdminCount => GetInt32("adminCount") == 1;

    public UserAccountControl AccountControl => (UserAccountControl)(GetInt32("userAccountControl") ?? 0);

    public string ParentDn => DistinguishedName.Parent(Dn);

    public override string ToString() => Dn;
}
