using Deckside.Core.Directory;
using Deckside.Core.Plans;

namespace Deckside.Directory;

/// <summary>
/// The last gate before the directory: every write is checked against the master OU right before it is
/// sent, whatever the planners decided. Reads pass through. It refuses:
/// <list type="bullet">
/// <item>any write if the master OU is not set, not in the connected domain, or does not exist;</item>
/// <item>creating, changing, moving or setting a password on an object that is not strictly inside the master OU;</item>
/// <item>moving to a container outside it; changing membership of a group (or with a member) outside it;</item>
/// <item>attributes that change security (ACLs, delegation, SPNs, SID history...) and dangerous
/// <c>userAccountControl</c> flags.</item>
/// </list>
/// </summary>
public sealed class GuardedDirectory : IDirectory
{
    /// <summary>Never written by Deckside, in any operation.</summary>
    public static readonly IReadOnlyList<string> ForbiddenAttributes =
    [
        "nTSecurityDescriptor", "member", "memberOf", "adminCount", "sIDHistory", "objectSid", "servicePrincipalName",
        "msDS-AllowedToDelegateTo", "msDS-AllowedToActOnBehalfOfOtherIdentity", "msDS-KeyCredentialLink",
        "altSecurityIdentities", "primaryGroupID", "scriptPath", "profilePath", "homeDirectory", "unicodePwd", "userPassword",
        "gPLink", "gPOptions", "msDS-SupportedEncryptionTypes", "userCertificate", "msDS-GroupMSAMembership",
    ];

    private const UserAccountControl DangerousFlags =
        UserAccountControl.TrustedForDelegation | UserAccountControl.TrustedToAuthForDelegation |
        UserAccountControl.DontRequirePreauth | UserAccountControl.ServerTrustAccount |
        UserAccountControl.UseDesKeyOnly | UserAccountControl.EncryptedTextPasswordAllowed;

    private readonly IDirectory inner;
    private readonly string masterOu;
    private bool masterVerified;

    public GuardedDirectory(IDirectory inner, string masterOu)
    {
        this.inner = inner;
        this.masterOu = masterOu;
    }

    public ConnectionInfo Info => inner.Info;

    /// <summary>Null when writes are possible; otherwise the code of the reason (shown in the top bar).</summary>
    public string? WriteBlockedReason()
    {
        if (!DistinguishedName.IsValid(masterOu))
        {
            return "MASTER_OU_NOT_CONFIGURED";
        }

        var nc = inner.Info.DefaultNamingContext;
        if (nc.Length > 0 && !(DistinguishedName.AreEqual(masterOu, nc) || DistinguishedName.IsDescendantOf(masterOu, nc)))
        {
            return "MASTER_OU_NOT_IN_DOMAIN";
        }

        if (!masterVerified)
        {
            if (inner.Get(masterOu, ["objectClass"]) is null)
            {
                return "MASTER_OU_NOT_FOUND";
            }

            masterVerified = true;
        }

        return null;
    }

    public DirectoryObject? Get(string dn, IReadOnlyList<string> attributes) => inner.Get(dn, attributes);

    public IReadOnlyList<DirectoryObject> Search(string baseDn, string filter, SearchDepth depth, IReadOnlyList<string> attributes, CancellationToken cancellation = default) =>
        inner.Search(baseDn, filter, depth, attributes, cancellation);

    public void Add(string dn, IReadOnlyList<AttributeChange> attributes)
    {
        RequireWritable();
        RequireInside(dn);
        var isComputer = attributes.Any(a => string.Equals(a.Name, "objectClass", StringComparison.OrdinalIgnoreCase) &&
                                             a.After.Contains("computer", StringComparer.OrdinalIgnoreCase));
        foreach (var a in attributes)
        {
            if (!string.Equals(a.Name, "objectClass", StringComparison.OrdinalIgnoreCase))
            {
                RequireAllowed(a.Name, a.After, allowPasswordNotRequired: isComputer);
            }
        }

        inner.Add(dn, attributes);
    }

    public void Modify(string dn, IReadOnlyList<AttributeModification> changes)
    {
        RequireWritable();
        RequireInside(dn);
        foreach (var c in changes)
        {
            if (string.Equals(c.Name, "objectClass", StringComparison.OrdinalIgnoreCase))
            {
                throw new DirectoryException("ATTRIBUTE_FORBIDDEN", c.Name);
            }

            // Disabling keeps the flags the account already had; only new dangerous flags are refused.
            RequireAllowed(c.Name, c.Values, allowPasswordNotRequired: true);
        }

        inner.Modify(dn, changes);
    }

    public void Move(string dn, string newParentDn)
    {
        RequireWritable();
        RequireInside(dn);
        if (!(DistinguishedName.AreEqual(newParentDn, masterOu) || IsInside(newParentDn)))
        {
            throw new DirectoryException("OUTSIDE_MASTER_OU", newParentDn);
        }

        inner.Move(dn, newParentDn);
    }

    public void AddMember(string groupDn, string memberDn)
    {
        RequireWritable();
        RequireInside(groupDn);
        RequireInside(memberDn);
        inner.AddMember(groupDn, memberDn);
    }

    public void RemoveMember(string groupDn, string memberDn)
    {
        RequireWritable();
        RequireInside(groupDn);
        RequireInside(memberDn);
        inner.RemoveMember(groupDn, memberDn);
    }

    public void SetPassword(string dn, string password)
    {
        RequireWritable();
        RequireInside(dn);
        if (inner.Info.Encryption.Length == 0)
        {
            throw new DirectoryException("NOT_ENCRYPTED", inner.Info.Server);
        }

        inner.SetPassword(dn, password);
    }

    public void Dispose() => inner.Dispose();

    private bool IsInside(string dn) =>
        DistinguishedName.IsValid(masterOu) && DistinguishedName.IsValid(dn) && DistinguishedName.IsDescendantOf(dn, masterOu);

    private void RequireInside(string dn)
    {
        if (!IsInside(dn))
        {
            throw new DirectoryException("OUTSIDE_MASTER_OU", dn);
        }
    }

    private void RequireWritable()
    {
        var reason = WriteBlockedReason();
        if (reason is not null)
        {
            throw new DirectoryException(reason, masterOu);
        }
    }

    private static void RequireAllowed(string attribute, IReadOnlyList<string> values, bool allowPasswordNotRequired)
    {
        if (ForbiddenAttributes.Contains(attribute, StringComparer.OrdinalIgnoreCase))
        {
            throw new DirectoryException("ATTRIBUTE_FORBIDDEN", attribute);
        }

        if (!string.Equals(attribute, "userAccountControl", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        foreach (var v in values)
        {
            if (!int.TryParse(v, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var n))
            {
                throw new DirectoryException("UAC_INVALID", v);
            }

            var flags = (UserAccountControl)n;
            if ((flags & DangerousFlags) != 0 || (!allowPasswordNotRequired && flags.HasFlag(UserAccountControl.PasswordNotRequired)))
            {
                throw new DirectoryException("UAC_FORBIDDEN", v);
            }
        }
    }
}
