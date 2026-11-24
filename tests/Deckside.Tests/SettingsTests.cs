using Deckside.Core.Settings;
using Deckside.Core.Time;
using Deckside.Tests.Support;

namespace Deckside.Tests;

public class SettingsTests
{
    [Fact]
    public void Empty_file_gives_defaults_without_warnings()
    {
        var r = SettingsLoader.Parse("");
        Assert.Empty(r.Warnings);
        Assert.Equal("es", r.Settings.Language);
        Assert.Equal("U{seq:5}", r.Settings.Naming.SamPattern);
    }

    [Fact]
    public void Invalid_values_fall_back_to_the_default_with_a_warning()
    {
        var json = """
        {
          "Language": "fr",
          "MasterOU": "DC=example,DC=local",
          "UsersOU": "not a dn",
          "UseLdaps": "yes",
          "Naming": { "SamPattern": "U{seq:30}", "ComputerPattern": "VERYLONGPREFIX{seq:5}", "MailPattern": "{given1}.{n}", "SamFirstNumber": 5 },
          "Report": { "InactiveDays": 0 },
          "DefaultComputerGroups": ["CN=ok,DC=example,DC=local", "bad"],
          "Password": "x",
          "Mystery": 1
        }
        """;
        var r = SettingsLoader.Parse(json);
        var keys = r.Warnings.Select(w => w.Key + ":" + w.Code).ToList();
        Assert.Contains("Language:INVALID_VALUE", keys);
        Assert.Contains("UsersOU:INVALID_VALUE", keys);
        Assert.Contains("UseLdaps:INVALID_TYPE", keys);
        Assert.Contains("Naming.SamPattern:INVALID_VALUE", keys);
        Assert.Contains("Naming.ComputerPattern:INVALID_VALUE", keys);
        Assert.Contains("Naming.MailPattern:INVALID_VALUE", keys);
        Assert.Contains("Report.InactiveDays:OUT_OF_RANGE", keys);
        Assert.Contains("DefaultComputerGroups:INVALID_ITEM", keys);
        Assert.Contains("Password:INVALID_TYPE", keys);
        Assert.Contains("Mystery:UNKNOWN_KEY", keys);
        Assert.Equal("es", r.Settings.Language);
        Assert.Equal("", r.Settings.UsersOU);
        Assert.Equal(5, r.Settings.Naming.SamFirstNumber);
        Assert.Equal(90, r.Settings.Report.InactiveDays);
        Assert.Single(r.Settings.DefaultComputerGroups);
    }

    [Fact]
    public void Invalid_json_is_a_warning_not_a_crash()
    {
        var r = SettingsLoader.Parse("{ nope");
        Assert.Equal("INVALID_JSON", Assert.Single(r.Warnings).Code);
    }

    [Fact]
    public void Disabled_ou_inside_users_ou_and_ou_of_another_domain_are_reported()
    {
        var json = $$"""
        { "Domain": "example.local", "MasterOU": "{{TestDomain.Root}}", "UsersOU": "{{TestDomain.UsersOu}}", "DisabledOU": "OU=Gone,{{TestDomain.UsersOu}}" }
        """;
        var codes = SettingsLoader.Parse(json).Warnings.Select(w => w.Code).ToList();
        Assert.Contains("DISABLED_OU_INSIDE_USERS_OU", codes);
        var other = SettingsLoader.Parse("""{ "Domain": "example.local", "MasterOU": "OU=X,DC=other,DC=local" }""");
        Assert.Contains(other.Warnings, w => w.Code == "OU_NOT_IN_DOMAIN" && w.Key == "MasterOU");
    }

    [Fact]
    public void Writer_and_loader_round_trip_and_never_write_credentials()
    {
        var json = SettingsWriter.ToJson(TestDomain.Settings);
        var r = SettingsLoader.Parse(json);
        Assert.Empty(r.Warnings);
        Assert.Equal(TestDomain.Settings with { Naming = r.Settings.Naming, DefaultComputerGroups = r.Settings.DefaultComputerGroups, ExtraProtectedGroups = r.Settings.ExtraProtectedGroups }, r.Settings);
        Assert.Equal(TestDomain.Settings.Naming.MailFallbacks, r.Settings.Naming.MailFallbacks);
        foreach (var word in new[] { "credential", "secret", "username", "\"value\"" })
        {
            Assert.DoesNotContain(word, json, StringComparison.OrdinalIgnoreCase);
        }
        Snapshot.Match("settings.default.json", SettingsWriter.ToJson(new AppSettings()));
    }

    [Fact]
    public void Evidence_folder_names()
    {
        var clock = new FixedClock(new DateTimeOffset(2026, 11, 24, 15, 0, 0, TimeSpan.Zero));
        var user = DataPaths.EvidenceFolderName(EvidenceKind.User, "U00042", clock);
        Assert.StartsWith("USER_U00042_2026112", user);
        var off = DataPaths.EvidenceFolderName(EvidenceKind.Offboarding, "INC/123 x", clock);
        Assert.StartsWith("OFFBOARD_2026112", off);
        Assert.EndsWith("_INC-123-x", off);
        Assert.Equal("", DataPaths.SafeFilePart("../"));
    }
}
