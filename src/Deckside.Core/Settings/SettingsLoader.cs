using System.Globalization;
using System.Text;
using System.Text.Json;
using Deckside.Core.Directory;
using Deckside.Core.Localization;
using Deckside.Core.Naming;

namespace Deckside.Core.Settings;

/// <summary>A value of settings.json that was ignored (the default is used instead). Codes are English.</summary>
public sealed record SettingsWarning(string Key, string Code, string? Value = null);

public sealed record SettingsResult(AppSettings Settings, IReadOnlyList<SettingsWarning> Warnings);

/// <summary>
/// Reads settings.json field by field: an invalid or unknown value does not stop the app, it falls back to
/// the default and becomes a warning that the Settings page shows.
/// </summary>
public static class SettingsLoader
{
    public static SettingsResult Parse(string? json)
    {
        var warnings = new List<SettingsWarning>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return new SettingsResult(new AppSettings(), warnings);
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException)
        {
            warnings.Add(new SettingsWarning("", "INVALID_JSON"));
            return new SettingsResult(new AppSettings(), warnings);
        }

        using (doc)
        {
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                warnings.Add(new SettingsWarning("", "INVALID_JSON"));
                return new SettingsResult(new AppSettings(), warnings);
            }

            var r = new Reader(doc.RootElement, "", warnings);
            var d = new AppSettings();
            var settings = new AppSettings
            {
                Language = r.String("Language", d.Language, v => Translator.IsSupported(v)).ToLowerInvariant(),
                DataFolder = r.String("DataFolder", d.DataFolder, _ => true),
                MasterOU = r.String("MasterOU", d.MasterOU, IsEmptyOrDn),
                Domain = r.String("Domain", d.Domain, v => v.Length == 0 || AccountNames.IsValidDnsDomain(v)),
                Server = r.String("Server", d.Server, v => v.Length == 0 || ServerAddress.IsValid(v)),
                Port = (int)r.Int64("Port", d.Port, 0, 65535),
                UseLdaps = r.Bool("UseLdaps", d.UseLdaps),
                UsersOU = r.String("UsersOU", d.UsersOU, IsEmptyOrDn),
                ComputersOU = r.String("ComputersOU", d.ComputersOU, IsEmptyOrDn),
                DisabledOU = r.String("DisabledOU", d.DisabledOU, IsEmptyOrDn),
                IncludeSubOUs = r.Bool("IncludeSubOUs", d.IncludeSubOUs),
                RequireTicket = r.Bool("RequireTicket", d.RequireTicket),
                Naming = ReadNaming(r.Object("Naming")),
                Password = ReadPassword(r.Object("Password")),
                Offboarding = ReadOffboarding(r.Object("Offboarding")),
                Report = ReadReport(r.Object("Report")),
                DefaultComputerGroups = r.StringList("DefaultComputerGroups", DistinguishedName.IsValid),
                ExtraProtectedGroups = r.StringList("ExtraProtectedGroups", v => v.Length > 0),
                ProfilesFile = r.String("ProfilesFile", d.ProfilesFile, v => v.Length > 0 && v.IndexOfAny(Path.GetInvalidPathChars()) < 0),
            };
            r.ReportUnknown();
            settings = EnforceMasterOu(settings, warnings);
            CrossChecks(settings, warnings);
            return new SettingsResult(settings, warnings);
        }
    }

    public static SettingsResult Load(string path) =>
        File.Exists(path) ? Parse(File.ReadAllText(path, Encoding.UTF8)) : new SettingsResult(new AppSettings(), []);

    private static NamingSettings ReadNaming(Reader? r)
    {
        var d = new NamingSettings();
        if (r is null)
        {
            return d;
        }

        var samPattern = r.String("SamPattern", d.SamPattern, v => SequencePattern.TryParse(v, out var p) && p!.Length <= AccountNames.MaxUserSamLength);
        var computerPattern = r.String("ComputerPattern", d.ComputerPattern, v => SequencePattern.TryParse(v, out var p) && p!.Length <= AccountNames.MaxComputerNameLength);
        var sam = SequencePattern.Parse(samPattern);
        var computer = SequencePattern.Parse(computerPattern);
        var result = new NamingSettings
        {
            SamPattern = samPattern,
            SamFirstNumber = r.Int64("SamFirstNumber", d.SamFirstNumber, 0, sam.MaxNumber),
            ComputerPattern = computerPattern,
            ComputerFirstNumber = r.Int64("ComputerFirstNumber", d.ComputerFirstNumber, 0, computer.MaxNumber),
            MailPattern = r.String("MailPattern", d.MailPattern, v => IsPattern(v, UserNameBuilder.MailTokens) && !PatternTemplate.Tokens(v).Contains("n")),
            MailFallbacks = r.StringList("MailFallbacks", v => IsPattern(v, UserNameBuilder.MailTokens), d.MailFallbacks),
            MailDomain = r.String("MailDomain", d.MailDomain, v => v.Length == 0 || AccountNames.IsValidDnsDomain(v)),
            SetProxyAddresses = r.Bool("SetProxyAddresses", d.SetProxyAddresses),
            UpnPattern = r.String("UpnPattern", d.UpnPattern, v => IsPattern(v, UserNameBuilder.UpnTokens) && v.Contains('@', StringComparison.Ordinal)),
            UpnSuffix = r.String("UpnSuffix", d.UpnSuffix, v => v.Length == 0 || AccountNames.IsValidDnsDomain(v)),
            CnPattern = r.String("CnPattern", d.CnPattern, v => IsPattern(v, UserNameBuilder.CnTokens)),
            DisplayNamePattern = r.String("DisplayNamePattern", d.DisplayNamePattern, v => IsPattern(v, UserNameBuilder.CnTokens)),
            SurnamePattern = r.String("SurnamePattern", d.SurnamePattern, v => IsPattern(v, UserNameBuilder.CnTokens)),
        };
        r.ReportUnknown();
        return result;
    }

    private static PasswordSettings ReadPassword(Reader? r)
    {
        var d = new PasswordSettings();
        if (r is null)
        {
            return d;
        }

        var result = new PasswordSettings
        {
            Generate = r.Bool("Generate", d.Generate),
            Length = (int)r.Int64("Length", d.Length, Security.PasswordGenerator.MinimumLength, Security.PasswordGenerator.MaximumLength),
            MinimumLength = (int)r.Int64("MinimumLength", d.MinimumLength, 8, 64),
        };
        r.ReportUnknown();
        return result;
    }

    public static readonly IReadOnlyList<string> OffboardingTokens = ["date", "ticket", "reason", "operator", "previous"];

    private static OffboardingSettings ReadOffboarding(Reader? r)
    {
        var d = new OffboardingSettings();
        if (r is null)
        {
            return d;
        }

        var result = new OffboardingSettings
        {
            DescriptionPattern = r.String("DescriptionPattern", d.DescriptionPattern, v => IsPattern(v, OffboardingTokens)),
        };
        r.ReportUnknown();
        return result;
    }

    private static ReportSettings ReadReport(Reader? r)
    {
        var d = new ReportSettings();
        if (r is null)
        {
            return d;
        }

        var result = new ReportSettings
        {
            InactiveDays = (int)r.Int64("InactiveDays", d.InactiveDays, 1, 3650),
            ExpiryWarningDays = (int)r.Int64("ExpiryWarningDays", d.ExpiryWarningDays, 0, 365),
            BadPasswordThreshold = (int)r.Int64("BadPasswordThreshold", d.BadPasswordThreshold, 1, 1000),
            MachinePasswordMaxDays = (int)r.Int64("MachinePasswordMaxDays", d.MachinePasswordMaxDays, 1, 3650),
            QueryAllDCs = r.Bool("QueryAllDCs", d.QueryAllDCs),
        };
        r.ReportUnknown();
        return result;
    }

    /// <summary>
    /// Every OU and default group must be inside the master OU. One that is not is dropped (empty), so the
    /// operations that need it refuse to run instead of writing outside.
    /// </summary>
    private static AppSettings EnforceMasterOu(AppSettings s, List<SettingsWarning> warnings)
    {
        var master = s.MasterOU;
        bool Inside(string dn) => master.Length > 0 && DistinguishedName.IsValid(dn) &&
                                  (DistinguishedName.AreEqual(dn, master) || DistinguishedName.IsDescendantOf(dn, master));

        string Check(string key, string ou)
        {
            if (ou.Length == 0 || Inside(ou))
            {
                return ou;
            }

            warnings.Add(new SettingsWarning(key, master.Length == 0 ? "MASTER_OU_NOT_CONFIGURED" : "OUTSIDE_MASTER_OU", ou));
            return "";
        }

        var groups = new List<string>();
        foreach (var g in s.DefaultComputerGroups)
        {
            if (Inside(g) && !DistinguishedName.AreEqual(g, master))
            {
                groups.Add(g);
            }
            else
            {
                warnings.Add(new SettingsWarning("DefaultComputerGroups", master.Length == 0 ? "MASTER_OU_NOT_CONFIGURED" : "OUTSIDE_MASTER_OU", g));
            }
        }

        return s with
        {
            UsersOU = Check("UsersOU", s.UsersOU),
            ComputersOU = Check("ComputersOU", s.ComputersOU),
            DisabledOU = Check("DisabledOU", s.DisabledOU),
            DefaultComputerGroups = groups,
        };
    }

    private static void CrossChecks(AppSettings s, List<SettingsWarning> warnings)
    {
        string[] ous = [s.MasterOU, s.UsersOU, s.ComputersOU, s.DisabledOU];
        string[] keys = ["MasterOU", "UsersOU", "ComputersOU", "DisabledOU"];
        for (var i = 0; i < ous.Length; i++)
        {
            if (ous[i].Length > 0 && s.Domain.Length > 0 &&
                !string.Equals(DistinguishedName.DomainOf(ous[i]), s.Domain, StringComparison.OrdinalIgnoreCase))
            {
                warnings.Add(new SettingsWarning(keys[i], "OU_NOT_IN_DOMAIN", ous[i]));
            }
        }

        // An offboarded user must leave the scope of the users OU, otherwise it can be offboarded again or edited.
        if (s.DisabledOU.Length > 0 && s.UsersOU.Length > 0 &&
            (DistinguishedName.AreEqual(s.DisabledOU, s.UsersOU) || DistinguishedName.IsDescendantOf(s.DisabledOU, s.UsersOU)))
        {
            warnings.Add(new SettingsWarning("DisabledOU", "DISABLED_OU_INSIDE_USERS_OU", s.DisabledOU));
        }
    }

    private static bool IsEmptyOrDn(string value) => value.Length == 0 || DistinguishedName.IsValid(value);

    private static bool IsPattern(string value, IReadOnlyCollection<string> tokens) => PatternTemplate.IsValid(value, tokens, out _);


    /// <summary>Reads typed members of one JSON object, remembering which keys were used.</summary>
    private sealed class Reader(JsonElement element, string path, List<SettingsWarning> warnings)
    {
        private readonly HashSet<string> used = new(StringComparer.OrdinalIgnoreCase);

        public string String(string key, string fallback, Func<string, bool> valid)
        {
            if (!TryGet(key, out var e))
            {
                return fallback;
            }

            if (e.ValueKind == JsonValueKind.String)
            {
                var value = e.GetString()!.Trim();
                if (valid(value))
                {
                    return value;
                }

                Warn(key, "INVALID_VALUE", value);
                return fallback;
            }

            Warn(key, "INVALID_TYPE", e.ToString());
            return fallback;
        }

        public bool Bool(string key, bool fallback)
        {
            if (!TryGet(key, out var e))
            {
                return fallback;
            }

            if (e.ValueKind is JsonValueKind.True or JsonValueKind.False)
            {
                return e.GetBoolean();
            }

            Warn(key, "INVALID_TYPE", e.ToString());
            return fallback;
        }

        public long Int64(string key, long fallback, long min, long max)
        {
            if (!TryGet(key, out var e))
            {
                return fallback;
            }

            if (e.ValueKind == JsonValueKind.Number && e.TryGetInt64(out var n))
            {
                if (n >= min && n <= max)
                {
                    return n;
                }

                Warn(key, "OUT_OF_RANGE", n.ToString(CultureInfo.InvariantCulture));
                return fallback;
            }

            Warn(key, "INVALID_TYPE", e.ToString());
            return fallback;
        }

        public IReadOnlyList<string> StringList(string key, Func<string, bool> valid, IReadOnlyList<string>? fallback = null)
        {
            if (!TryGet(key, out var e))
            {
                return fallback ?? [];
            }

            if (e.ValueKind != JsonValueKind.Array)
            {
                Warn(key, "INVALID_TYPE", e.ToString());
                return fallback ?? [];
            }

            var result = new List<string>();
            foreach (var item in e.EnumerateArray())
            {
                var value = item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : null;
                if (value is not null && valid(value))
                {
                    result.Add(value);
                }
                else
                {
                    Warn(key, "INVALID_ITEM", item.ToString());
                }
            }

            return result;
        }

        public Reader? Object(string key)
        {
            if (!TryGet(key, out var e))
            {
                return null;
            }

            if (e.ValueKind == JsonValueKind.Object)
            {
                return new Reader(e, path + key + ".", warnings);
            }

            Warn(key, "INVALID_TYPE", e.ToString());
            return null;
        }

        public void ReportUnknown()
        {
            foreach (var p in element.EnumerateObject())
            {
                if (!used.Contains(p.Name))
                {
                    warnings.Add(new SettingsWarning(path + p.Name, "UNKNOWN_KEY"));
                }
            }
        }

        private bool TryGet(string key, out JsonElement value)
        {
            used.Add(key);
            foreach (var p in element.EnumerateObject())
            {
                if (string.Equals(p.Name, key, StringComparison.OrdinalIgnoreCase) && p.Value.ValueKind != JsonValueKind.Null)
                {
                    value = p.Value;
                    return true;
                }
            }

            value = default;
            return false;
        }

        private void Warn(string key, string code, string? value) => warnings.Add(new SettingsWarning(path + key, code, value));
    }
}

/// <summary>Writes settings.json (UTF-8 without BOM, indented, stable key order).</summary>
public static class SettingsWriter
{
    public static string ToJson(AppSettings s)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("Language", s.Language);
            w.WriteString("DataFolder", s.DataFolder);
            w.WriteString("Server", s.Server);
            w.WriteNumber("Port", s.Port);
            w.WriteBoolean("UseLdaps", s.UseLdaps);
            w.WriteString("Domain", s.Domain);
            w.WriteString("MasterOU", s.MasterOU);
            w.WriteString("UsersOU", s.UsersOU);
            w.WriteString("ComputersOU", s.ComputersOU);
            w.WriteString("DisabledOU", s.DisabledOU);
            w.WriteBoolean("IncludeSubOUs", s.IncludeSubOUs);
            w.WriteBoolean("RequireTicket", s.RequireTicket);

            w.WriteStartObject("Naming");
            var n = s.Naming;
            w.WriteString("SamPattern", n.SamPattern);
            w.WriteNumber("SamFirstNumber", n.SamFirstNumber);
            w.WriteString("ComputerPattern", n.ComputerPattern);
            w.WriteNumber("ComputerFirstNumber", n.ComputerFirstNumber);
            w.WriteString("MailPattern", n.MailPattern);
            WriteList(w, "MailFallbacks", n.MailFallbacks);
            w.WriteString("MailDomain", n.MailDomain);
            w.WriteBoolean("SetProxyAddresses", n.SetProxyAddresses);
            w.WriteString("UpnPattern", n.UpnPattern);
            w.WriteString("UpnSuffix", n.UpnSuffix);
            w.WriteString("CnPattern", n.CnPattern);
            w.WriteString("DisplayNamePattern", n.DisplayNamePattern);
            w.WriteString("SurnamePattern", n.SurnamePattern);
            w.WriteEndObject();

            w.WriteStartObject("Password");
            w.WriteBoolean("Generate", s.Password.Generate);
            w.WriteNumber("Length", s.Password.Length);
            w.WriteNumber("MinimumLength", s.Password.MinimumLength);
            w.WriteEndObject();

            w.WriteStartObject("Offboarding");
            w.WriteString("DescriptionPattern", s.Offboarding.DescriptionPattern);
            w.WriteEndObject();

            w.WriteStartObject("Report");
            w.WriteNumber("InactiveDays", s.Report.InactiveDays);
            w.WriteNumber("ExpiryWarningDays", s.Report.ExpiryWarningDays);
            w.WriteNumber("BadPasswordThreshold", s.Report.BadPasswordThreshold);
            w.WriteNumber("MachinePasswordMaxDays", s.Report.MachinePasswordMaxDays);
            w.WriteBoolean("QueryAllDCs", s.Report.QueryAllDCs);
            w.WriteEndObject();

            WriteList(w, "DefaultComputerGroups", s.DefaultComputerGroups);
            WriteList(w, "ExtraProtectedGroups", s.ExtraProtectedGroups);
            w.WriteString("ProfilesFile", s.ProfilesFile);
            w.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray()).Replace("\r\n", "\n", StringComparison.Ordinal) + "\n";
    }

    private static void WriteList(Utf8JsonWriter w, string name, IEnumerable<string> values)
    {
        w.WriteStartArray(name);
        foreach (var v in values)
        {
            w.WriteStringValue(v);
        }

        w.WriteEndArray();
    }
}
