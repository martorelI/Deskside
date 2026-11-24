using Deckside.Core.Directory;
using Deckside.Core.Plans;

namespace Deckside.Directory;

public enum SearchDepth
{
    Base,
    OneLevel,
    Subtree,
}

public enum ModificationKind
{
    Replace,
    Add,
    Delete,
}

/// <summary>One change on an attribute of an existing object.</summary>
public sealed record AttributeModification(string Name, ModificationKind Kind, IReadOnlyList<string> Values)
{
    public static AttributeModification Replace(string name, params string[] values) => new(name, ModificationKind.Replace, values);
}

/// <summary>
/// What Deckside can do with the directory. There is no delete on purpose (security rule 2). Reads can go
/// anywhere; writes are only accepted by <see cref="GuardedDirectory"/> inside the master OU.
/// </summary>
public interface IDirectory : IDisposable
{
    ConnectionInfo Info { get; }

    DirectoryObject? Get(string dn, IReadOnlyList<string> attributes);

    /// <summary>Paged search (1000 per page) that requests only <paramref name="attributes"/>.</summary>
    IReadOnlyList<DirectoryObject> Search(string baseDn, string filter, SearchDepth depth, IReadOnlyList<string> attributes, CancellationToken cancellation = default);

    void Add(string dn, IReadOnlyList<AttributeChange> attributes);

    void Modify(string dn, IReadOnlyList<AttributeModification> changes);

    /// <summary>Moves the object to <paramref name="newParentDn"/> keeping its RDN.</summary>
    void Move(string dn, string newParentDn);

    void AddMember(string groupDn, string memberDn);

    void RemoveMember(string groupDn, string memberDn);

    /// <summary>Sets <c>unicodePwd</c>. Only over an encrypted connection; the value is never logged.</summary>
    void SetPassword(string dn, string password);
}

/// <summary>What the operator sees about the connection (top bar and records).</summary>
public sealed record ConnectionInfo
{
    /// <summary>What the operator typed: IP address or host name.</summary>
    public required string Server { get; init; }

    public int Port { get; init; }

    /// <summary>Host name the DC reports about itself (rootDSE <c>dnsHostName</c>).</summary>
    public string DcHostName { get; init; } = "";

    /// <summary>rootDSE <c>defaultNamingContext</c>, e.g. <c>DC=example,DC=local</c>.</summary>
    public string DefaultNamingContext { get; init; } = "";

    public string DomainDns => DefaultNamingContext.Length > 0 ? DistinguishedName.DomainOf(DefaultNamingContext) : "";

    /// <summary>"LDAPS" or "SIGN_SEAL". Never empty: an unencrypted connection is refused.</summary>
    public string Encryption { get; init; } = "";

    /// <summary>The account the server sees (LDAP "Who am I?"), e.g. <c>EXAMPLE\operator1</c>.</summary>
    public string BoundAs { get; init; } = "";

    public bool UsesCurrentWindowsSession { get; init; }
}

/// <summary>A connection or directory error with an English code the UI translates.</summary>
public sealed class DirectoryException(string code, string detail, Exception? inner = null) : Exception(code + ": " + detail, inner)
{
    public string Code { get; } = code;

    public string Detail { get; } = detail;
}
