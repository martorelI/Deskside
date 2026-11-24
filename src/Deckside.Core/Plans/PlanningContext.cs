using Deckside.Core.Directory;
using Deckside.Core.Localization;
using Deckside.Core.Profiles;
using Deckside.Core.Settings;
using Deckside.Core.Time;

namespace Deckside.Core.Plans;

/// <summary>What every planner needs: settings, texts, the domain-wide uniqueness checks, profiles and groups.</summary>
public sealed record PlanningContext(
    AppSettings Settings,
    Translator T,
    IUniquenessChecker Checker,
    ProfileCatalog Profiles,
    Func<string, DirectoryObject?> FindGroup,
    IClock Clock)
{
    public Scope Scope { get; } = new(Settings);

    public string MailDomain => Settings.Naming.MailDomain.Length > 0 ? Settings.Naming.MailDomain : Settings.EffectiveDomain;

    public string UpnSuffix => Settings.Naming.UpnSuffix.Length > 0 ? Settings.Naming.UpnSuffix : Settings.EffectiveDomain;

    /// <summary>
    /// Resolves and checks the groups whose membership will change: they must be inside the master OU (the
    /// membership is written on the group object), exist, be groups and never be protected (security rule 3).
    /// Adds an error per bad group and returns the good ones (normalized DNs).
    /// </summary>
    public IReadOnlyList<string> CheckGroupsToAdd(IEnumerable<string> groupDns, IssueList issues)
    {
        var result = new List<string>();
        foreach (var dn in groupDns.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!DistinguishedName.IsValid(dn))
            {
                issues.Error("GROUP_INVALID_DN", T.T("Invalid group DN: {0}", dn));
                continue;
            }

            if (!Scope.IsInsideMaster(dn))
            {
                issues.Error("GROUP_OUTSIDE_MASTER_OU", T.T("The group is outside the master OU, Deckside does not change it: {0}", dn));
                continue;
            }

            var group = FindGroup(dn);
            if (group is null || !group.IsGroup)
            {
                issues.Error("GROUP_NOT_FOUND", T.T("Group not found: {0}", dn));
            }
            else if (ProtectedObjects.IsProtectedGroup(group, Settings.ExtraProtectedGroups))
            {
                issues.Error("PROTECTED_GROUP", T.T("Protected group, Deckside never changes its members: {0}", dn));
            }
            else
            {
                result.Add(group.Dn);
            }
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>Groups of the named profiles, checking that each profile exists and applies to <paramref name="target"/>.</summary>
    public IReadOnlyList<string> GroupsOfProfiles(IEnumerable<string> profileNames, ProfileTarget target, IssueList issues)
    {
        var groups = new List<string>();
        foreach (var name in profileNames)
        {
            var profile = Profiles.Find(name);
            if (profile is null)
            {
                issues.Error("PROFILE_NOT_FOUND", T.T("Profile not found: {0}", name));
            }
            else if (profile.Applies != target)
            {
                issues.Error("PROFILE_WRONG_TARGET", T.T("The profile {0} is not for this kind of object.", name));
            }
            else
            {
                groups.AddRange(profile.Groups);
            }
        }

        return groups;
    }
}
