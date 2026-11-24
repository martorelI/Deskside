using Deckside.Core.Directory;
using Deckside.Core.Settings;

namespace Deckside.Core.Plans;

public enum ScopeArea
{
    Users,
    Computers,
    Disabled,
}

/// <summary>
/// Security rule 1: writes only inside the master OU, and for each operation only inside its own OU (users,
/// computers, disabled). Checked against the DN of the object when planning and again right before writing
/// (the object may have been moved meanwhile). The directory layer repeats the master-OU check on every request.
/// </summary>
public sealed class Scope(AppSettings settings)
{
    public string MasterOu => settings.MasterOU;

    public bool IsMasterConfigured => DistinguishedName.IsValid(settings.MasterOU);

    /// <summary>True when <paramref name="dn"/> is strictly below the master OU (the master OU itself is never written).</summary>
    public bool IsInsideMaster(string dn) =>
        IsMasterConfigured && DistinguishedName.IsValid(dn) && DistinguishedName.IsDescendantOf(dn, settings.MasterOU);

    public string OuOf(ScopeArea area) => area switch
    {
        ScopeArea.Users => settings.UsersOU,
        ScopeArea.Computers => settings.ComputersOU,
        ScopeArea.Disabled => settings.DisabledOU,
        _ => throw new ArgumentOutOfRangeException(nameof(area)),
    };

    /// <summary>The OU is set and lies inside the master OU (or is the master OU itself).</summary>
    public bool IsConfigured(ScopeArea area)
    {
        var ou = OuOf(area);
        return IsMasterConfigured && DistinguishedName.IsValid(ou) &&
               (DistinguishedName.AreEqual(ou, settings.MasterOU) || IsInsideMaster(ou));
    }

    /// <summary>True when <paramref name="dn"/> is an object inside the OU of <paramref name="area"/> (and so inside the master OU).</summary>
    public bool Contains(ScopeArea area, string dn)
    {
        if (!IsConfigured(area) || !IsInsideMaster(dn))
        {
            return false;
        }

        var ou = OuOf(area);
        // The disabled OU is a destination: only direct children are "inside" it, whatever IncludeSubOUs says.
        return area != ScopeArea.Disabled && settings.IncludeSubOUs
            ? DistinguishedName.IsDescendantOf(dn, ou)
            : DistinguishedName.IsChildOf(dn, ou);
    }

    /// <summary>True when <paramref name="containerDn"/> is a valid place to create objects of <paramref name="area"/>.</summary>
    public bool IsValidTarget(ScopeArea area, string containerDn)
    {
        if (!IsConfigured(area) || !DistinguishedName.IsValid(containerDn))
        {
            return false;
        }

        var ou = OuOf(area);
        return DistinguishedName.AreEqual(containerDn, ou) ||
               (area != ScopeArea.Disabled && settings.IncludeSubOUs && DistinguishedName.IsDescendantOf(containerDn, ou));
    }
}
