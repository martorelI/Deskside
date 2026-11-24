using Deckside.Core.Directory;

namespace Deckside.Core.Profiles;

/// <summary>Result of comparing the current groups of an object with the groups of its new profile(s).</summary>
public sealed record GroupDifference(
    IReadOnlyList<string> ToAdd,
    IReadOnlyList<string> ToRemove,
    IReadOnlyList<string> Keep,
    IReadOnlyList<string> Untouched)
{
    public bool IsEmpty => ToAdd.Count == 0 && ToRemove.Count == 0;
}

public static class ProfileDiff
{
    /// <summary>
    /// Only managed groups change (security rule 9): a managed group that the object has and the target does
    /// not is removed; a target group it does not have is added. Every other group is untouched. The primary
    /// group never appears in <c>memberOf</c>, so it is never in <paramref name="currentGroupDns"/>; if a target
    /// group is the primary group (<paramref name="primaryGroupDn"/>) it is kept and never added.
    /// </summary>
    public static GroupDifference Compute(IEnumerable<string> currentGroupDns, IEnumerable<GroupProfile> targets, ProfileCatalog catalog, string? primaryGroupDn = null)
    {
        var current = currentGroupDns.Select(DistinguishedName.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var target = targets.SelectMany(p => p.Groups).Select(DistinguishedName.Normalize).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        bool IsPrimary(string dn) => primaryGroupDn is not null && DistinguishedName.AreEqual(dn, primaryGroupDn);
        bool In(List<string> list, string dn) => list.Contains(dn, StringComparer.OrdinalIgnoreCase);

        var toAdd = target.Where(g => !In(current, g) && !IsPrimary(g)).ToList();
        var toRemove = current.Where(g => catalog.IsManaged(g) && !In(target, g)).ToList();
        var keep = current.Where(g => In(target, g)).Concat(target.Where(IsPrimary)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var untouched = current.Where(g => !catalog.IsManaged(g) && !In(target, g)).ToList();
        return new GroupDifference(Sorted(toAdd), Sorted(toRemove), Sorted(keep), Sorted(untouched));
    }

    private static List<string> Sorted(IEnumerable<string> dns) => [.. dns.Order(StringComparer.OrdinalIgnoreCase)];
}
