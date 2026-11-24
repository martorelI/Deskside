using Deckside.Core.Security;

namespace Deckside.Tests;

public class SecretMaskerTests
{
    [Fact]
    public void Masks_every_registered_secret_longest_first()
    {
        var m = new SecretMasker();
        m.Register("abc");
        m.Register("abcdef");
        m.Register("");
        Assert.Equal("pwd=******** and ********", m.Apply("pwd=abcdef and abc"));
        Assert.True(SecretMasker.IsSecretAttribute("UNICODEPWD"));
    }
}

public class PasswordTests
{
    [Fact]
    public void Generated_passwords_meet_the_rules_and_differ()
    {
        var seen = new HashSet<string>();
        for (var i = 0; i < 200; i++)
        {
            var p = PasswordGenerator.Generate(16);
            Assert.Equal(16, p.Length);
            // 'o' and '0' are never generated, so these names cannot appear by chance in a random password.
            Assert.Empty(PasswordRules.Problems(p, 12, "U00042", "Oto Polo"));
            Assert.DoesNotContain('0', p);
            Assert.DoesNotContain('l', p);
            seen.Add(p);
        }

        Assert.Equal(200, seen.Count);
        Assert.Throws<ArgumentOutOfRangeException>(() => PasswordGenerator.Generate(8));
    }

    [Fact]
    public void Typed_password_rules()
    {
        Assert.Contains("TOO_SHORT", PasswordRules.Problems("Ab1!", 12, "U00042", "Ana Ruiz"));
        Assert.Contains("NOT_COMPLEX", PasswordRules.Problems("abcdefghijklmn", 12, "U00042", "Ana Ruiz"));
        Assert.Contains("CONTAINS_ACCOUNT", PasswordRules.Problems("xxU00042Yy!!zz", 12, "U00042", "Ana Ruiz"));
        Assert.Contains("CONTAINS_NAME", PasswordRules.Problems("Ruiz-2026-Good!", 12, "U00042", "Ana Ruiz"));
    }
}
