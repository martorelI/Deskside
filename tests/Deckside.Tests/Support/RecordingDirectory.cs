using Deckside.Core.Directory;
using Deckside.Core.Plans;
using Deckside.Directory;

namespace Deckside.Tests.Support;

/// <summary>
/// In-memory <see cref="IDirectory"/> over a <see cref="TestDomain"/> that records every write that reaches it
/// (so a test can prove that a refused write never got to the directory). Searches ignore the filter.
/// The complete FakeDirectory comes with phase 2.
/// </summary>
internal sealed class RecordingDirectory(TestDomain domain) : IDirectory
{
    public List<string> Writes { get; } = [];

    public ConnectionInfo Info { get; init; } = new()
    {
        Server = "10.0.0.10",
        Port = 389,
        DcHostName = "dc01.example.local",
        DefaultNamingContext = TestDomain.Base,
        Encryption = "SIGN_SEAL",
        BoundAs = "EXAMPLE\\operator1",
    };

    public bool MasterExists { get; set; } = true;

    public DirectoryObject? Get(string dn, IReadOnlyList<string> attributes)
    {
        if (DistinguishedName.AreEqual(dn, TestDomain.Root))
        {
            return MasterExists ? TestDomain.Obj(dn, ("objectClass", ["organizationalUnit"])) : null;
        }

        return domain.Objects.FirstOrDefault(o => DistinguishedName.AreEqual(o.Dn, dn));
    }

    public IReadOnlyList<DirectoryObject> Search(string baseDn, string filter, SearchDepth depth, IReadOnlyList<string> attributes, CancellationToken cancellation = default) =>
        [.. domain.Objects.Where(o => depth switch
        {
            SearchDepth.Base => DistinguishedName.AreEqual(o.Dn, baseDn),
            SearchDepth.OneLevel => DistinguishedName.IsChildOf(o.Dn, baseDn),
            _ => DistinguishedName.IsDescendantOf(o.Dn, baseDn),
        })];

    public void Add(string dn, IReadOnlyList<AttributeChange> attributes) => Writes.Add("ADD " + dn);

    public void Modify(string dn, IReadOnlyList<AttributeModification> changes) =>
        Writes.Add("MODIFY " + dn + " " + string.Join(",", changes.Select(c => c.Name)));

    public void Move(string dn, string newParentDn) => Writes.Add("MOVE " + dn + " -> " + newParentDn);

    public void AddMember(string groupDn, string memberDn) => Writes.Add("ADDMEMBER " + groupDn + " " + memberDn);

    public void RemoveMember(string groupDn, string memberDn) => Writes.Add("REMOVEMEMBER " + groupDn + " " + memberDn);

    public void SetPassword(string dn, string password) => Writes.Add("PASSWORD " + dn);

    public void Dispose()
    {
    }
}
