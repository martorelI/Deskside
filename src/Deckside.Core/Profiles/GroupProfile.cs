using System.Text.Json;
using Deckside.Core.Directory;

namespace Deckside.Core.Profiles;

public enum ProfileTarget
{
    User,
    Computer,
}

/// <summary>A named template of security groups (e.g. "Accounting", "Kiosk PC").</summary>
public sealed record GroupProfile(string Name, ProfileTarget Applies, IReadOnlyList<string> Groups);

/// <summary>A problem found while loading or resolving the profiles. Codes are English.</summary>
public sealed record ProfileIssue(string Profile, string Code, string? Detail = null);

/// <summary>
/// The loaded profiles. "Managed groups" are the groups that belong to at least one profile: they are the only
/// ones Deckside adds or removes when it assigns or changes a profile (security rule 9).
/// </summary>
public sealed class ProfileCatalog
{
    private readonly HashSet<string> managed = new(StringComparer.OrdinalIgnoreCase);

    public ProfileCatalog(IEnumerable<GroupProfile> profiles)
    {
        Profiles = [.. profiles];
        foreach (var g in Profiles.SelectMany(p => p.Groups))
        {
            managed.Add(DistinguishedName.Normalize(g));
        }
    }

    public static ProfileCatalog Empty { get; } = new([]);

    public IReadOnlyList<GroupProfile> Profiles { get; }

    public IReadOnlyCollection<string> ManagedGroups => managed;

    public bool IsManaged(string groupDn) => DistinguishedName.IsValid(groupDn) && managed.Contains(DistinguishedName.Normalize(groupDn));

    public GroupProfile? Find(string name) =>
        Profiles.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Second half of loading (security rules 1 and 3): every group must be inside the master OU, exist, be a
    /// group and not be protected.
    /// A profile with any problem is dropped from the returned catalog and reported.
    /// </summary>
    public (ProfileCatalog Catalog, IReadOnlyList<ProfileIssue> Issues) Resolve(Func<string, DirectoryObject?> findGroup, string masterOu, IEnumerable<string>? extraProtectedNames = null)
    {
        var issues = new List<ProfileIssue>();
        var extra = extraProtectedNames?.ToList() ?? [];
        var kept = new List<GroupProfile>();
        foreach (var profile in Profiles)
        {
            var ok = true;
            foreach (var dn in profile.Groups)
            {
                if (!DistinguishedName.IsValid(masterOu) || !DistinguishedName.IsDescendantOf(dn, masterOu))
                {
                    issues.Add(new ProfileIssue(profile.Name, "OUTSIDE_MASTER_OU", dn));
                    ok = false;
                    continue;
                }

                var group = findGroup(dn);
                if (group is null)
                {
                    issues.Add(new ProfileIssue(profile.Name, "GROUP_NOT_FOUND", dn));
                    ok = false;
                }
                else if (!group.IsGroup)
                {
                    issues.Add(new ProfileIssue(profile.Name, "NOT_A_GROUP", dn));
                    ok = false;
                }
                else if (ProtectedObjects.IsProtectedGroup(group, extra))
                {
                    issues.Add(new ProfileIssue(profile.Name, "PROTECTED_GROUP", dn));
                    ok = false;
                }
            }

            if (ok)
            {
                kept.Add(profile);
            }
        }

        // Groups of rejected profiles stay managed: otherwise changing a profile could leave them behind silently.
        return (new ProfileCatalog(kept, managed), issues);
    }

    private ProfileCatalog(IEnumerable<GroupProfile> profiles, IEnumerable<string> managedGroups)
        : this(profiles)
    {
        foreach (var g in managedGroups)
        {
            managed.Add(g);
        }
    }
}

/// <summary>
/// Reads <c>profiles.json</c>: <c>[{ "Name": "...", "Applies": "User|Computer", "Groups": ["&lt;DN&gt;", ...] }]</c>.
/// Syntax problems drop the profile and are reported; protected groups are checked by <see cref="ProfileCatalog.Resolve"/>.
/// </summary>
public static class ProfileFile
{
    public static (ProfileCatalog Catalog, IReadOnlyList<ProfileIssue> Issues) Parse(string? json)
    {
        var issues = new List<ProfileIssue>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return (ProfileCatalog.Empty, issues);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            issues.Add(new ProfileIssue("", "INVALID_JSON"));
            return (ProfileCatalog.Empty, issues);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
            {
                issues.Add(new ProfileIssue("", "INVALID_JSON"));
                return (ProfileCatalog.Empty, issues);
            }

            var profiles = new List<GroupProfile>();
            var index = 0;
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                index++;
                var label = "#" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (item.ValueKind != JsonValueKind.Object)
                {
                    issues.Add(new ProfileIssue(label, "INVALID_ENTRY"));
                    continue;
                }

                var name = GetString(item, "Name")?.Trim();
                if (string.IsNullOrEmpty(name))
                {
                    issues.Add(new ProfileIssue(label, "MISSING_NAME"));
                    continue;
                }

                if (profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    issues.Add(new ProfileIssue(name, "DUPLICATED_NAME"));
                    continue;
                }

                if (!Enum.TryParse<ProfileTarget>(GetString(item, "Applies"), ignoreCase: true, out var applies) || !Enum.IsDefined(applies))
                {
                    issues.Add(new ProfileIssue(name, "INVALID_APPLIES"));
                    continue;
                }

                var groups = new List<string>();
                var valid = TryGetProperty(item, "Groups", out var groupsElement) && groupsElement.ValueKind == JsonValueKind.Array;
                if (valid)
                {
                    foreach (var g in groupsElement.EnumerateArray())
                    {
                        var dn = g.ValueKind == JsonValueKind.String ? g.GetString()!.Trim() : null;
                        if (dn is null || !DistinguishedName.IsValid(dn))
                        {
                            issues.Add(new ProfileIssue(name, "INVALID_GROUP_DN", g.ToString()));
                            valid = false;
                        }
                        else if (groups.Any(x => DistinguishedName.AreEqual(x, dn)))
                        {
                            issues.Add(new ProfileIssue(name, "DUPLICATED_GROUP", dn));
                        }
                        else
                        {
                            groups.Add(DistinguishedName.Normalize(dn));
                        }
                    }
                }
                else
                {
                    issues.Add(new ProfileIssue(name, "MISSING_GROUPS"));
                }

                if (valid && groups.Count > 0)
                {
                    profiles.Add(new GroupProfile(name, applies, groups));
                }
                else if (valid)
                {
                    issues.Add(new ProfileIssue(name, "MISSING_GROUPS"));
                }
            }

            return (new ProfileCatalog(profiles), issues);
        }
    }

    private static string? GetString(JsonElement e, string name) =>
        TryGetProperty(e, name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool TryGetProperty(JsonElement e, string name, out JsonElement value)
    {
        foreach (var p in e.EnumerateObject())
        {
            if (string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = p.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
