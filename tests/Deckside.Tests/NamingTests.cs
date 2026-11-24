using Deckside.Core.Naming;

namespace Deckside.Tests;

public class SequenceTests
{
    [Fact]
    public void Next_after_highest_in_the_whole_list_never_fills_holes()
    {
        var p = SequencePattern.Parse("U{seq:5}");
        var existing = new[] { new ExistingName("U00001", "a"), new ExistingName("u00041", "b"), new ExistingName("U0099", "c"), new ExistingName("U00007", "d") };
        var next = Sequential.Next(p, 1, existing);
        Assert.Equal("U00042", next.Name);
        Assert.Equal("u00041", next.Previous!.Name);
    }

    [Fact]
    public void First_number_and_skips_taken_names()
    {
        var p = SequencePattern.Parse("EMP{seq:4}X");
        var next = Sequential.Next(p, 100, [], name => name is "EMP0100X" or "EMP0101X");
        Assert.Equal("EMP0102X", next.Name);
        Assert.Equal(2, next.SkippedTaken);
        Assert.Null(next.Previous);
    }

    [Fact]
    public void Exhausted_sequence_throws()
    {
        var p = SequencePattern.Parse("A{seq:2}");
        var ex = Assert.Throws<NamingException>(() => Sequential.Next(p, 0, [new ExistingName("A99", "x")]));
        Assert.Equal("SEQUENCE_EXHAUSTED", ex.Code);
    }

    [Theory]
    [InlineData("U00001")]
    [InlineData("U{seq:5}{seq:2}")]
    [InlineData("U1{seq:3}")]
    [InlineData("U{seq:3}9")]
    [InlineData("U{seq:0}")]
    [InlineData("U{seq:11}")]
    public void Invalid_patterns(string pattern) => Assert.False(SequencePattern.TryParse(pattern, out _));
}

public class NormalizerTests
{
    [Theory]
    [InlineData("José", "jose")]
    [InlineData("Peña", "pena")]
    [InlineData("Müller", "muller")]
    [InlineData("De la  Cruz", "delacruz")]
    [InlineData("O'Connor", "oconnor")]
    [InlineData("María-José", "mariajose")]
    [InlineData("Straße", "strasse")]
    [InlineData("Łukasz", "lukasz")]
    [InlineData("  ", "")]
    public void Ascii_lower(string input, string expected) => Assert.Equal(expected, NameNormalizer.ToAsciiLower(input));

    [Theory]
    [InlineData("José Luis", true)]
    [InlineData("O'Connor", true)]
    [InlineData("Ana2", false)]
    [InlineData("a@b", false)]
    public void Person_name_characters(string value, bool ok) => Assert.Equal(ok, NameNormalizer.IsValidPersonName(value));
}

public class MailTests
{
    private static readonly string[] Fallbacks = ["{given1}.{surname1}{s2}", "{given1}.{surname1}{n}"];

    [Fact]
    public void Main_pattern_first_given_name_and_first_surname()
    {
        var m = UserNameBuilder.BuildMail(new PersonName("José Luis", "Peña", "Gómez"), null, "{given1}.{surname1}", Fallbacks, "Example.local", _ => false);
        Assert.Equal("jose.pena@example.local", m.Address);
        Assert.False(m.UsedFallback);
    }

    [Fact]
    public void Collision_uses_second_surname_initial_then_numbers()
    {
        var taken = new HashSet<string> { "jose.pena@example.local", "jose.penag@example.local", "jose.pena2@example.local" };
        var m = UserNameBuilder.BuildMail(new PersonName("José", "Peña", "Gómez"), null, "{given1}.{surname1}", Fallbacks, "example.local", taken.Contains);
        Assert.Equal("jose.pena3@example.local", m.Address);
        Assert.Equal(2, m.CandidateIndex);
        Assert.Equal(3, m.TakenCandidates.Count);
    }

    [Fact]
    public void Without_second_surname_the_repeated_candidate_is_skipped()
    {
        var taken = new HashSet<string> { "ana.ruiz@example.local" };
        var m = UserNameBuilder.BuildMail(new PersonName("Ana", "Ruiz"), null, "{given1}.{surname1}", Fallbacks, "example.local", taken.Contains);
        Assert.Equal("ana.ruiz2@example.local", m.Address);
        Assert.Single(m.TakenCandidates);
    }

    [Fact]
    public void Empty_tokens_do_not_leave_separators()
    {
        var m = UserNameBuilder.BuildMail(new PersonName("Ana", "Ruiz"), null, "{surname2}.{given1}.{surname1}.", [], "example.local", _ => false);
        Assert.Equal("ana.ruiz@example.local", m.Address);
    }

    [Fact]
    public void Exhausted()
    {
        var ex = Assert.Throws<NamingException>(() =>
            UserNameBuilder.BuildMail(new PersonName("Ana", "Ruiz"), null, "{given1}.{surname1}", [], "example.local", _ => true));
        Assert.Equal("MAIL_EXHAUSTED", ex.Code);
    }

    [Fact]
    public void Upn_and_display()
    {
        Assert.Equal("U00042@example.local", UserNameBuilder.BuildUpn("{sam}@{upnSuffix}", "U00042", "ana.ruiz", "example.local", "Example.Local"));
        Assert.Equal("ana.ruiz@example.local", UserNameBuilder.BuildUpn("{mailLocal}@{mailDomain}", "U00042", "ana.ruiz", "example.local", "x.local"));
        var name = new PersonName(" José  Luis ", "de la Peña", "");
        Assert.Equal("José Luis de la Peña", UserNameBuilder.BuildDisplay("{Given} {Surnames}", name, null));
        Assert.Equal("de la Peña, José Luis", UserNameBuilder.BuildDisplay("{Surnames}, {Given}", name, null));
        Assert.Equal("de la Peña", UserNameBuilder.BuildDisplay("{Surname1} {Surname2}", name, null));
    }
}

public class AccountNameTests
{
    [Theory]
    [InlineData("U00001", true)]
    [InlineData("ABCDEFGHIJKLMNOPQRSTU", false)] // 21
    [InlineData("a/b", false)]
    [InlineData("name.", false)]
    [InlineData("a,b", false)]
    public void User_sam(string sam, bool ok) => Assert.Equal(ok, AccountNames.UserSamProblems(sam).Count == 0);

    [Theory]
    [InlineData("PC00001", true)]
    [InlineData("PC-ACCT-01", true)]
    [InlineData("1234", false)]
    [InlineData("PC_01", false)]
    [InlineData("ABCDEFGHIJKLMNOP", false)] // 16
    [InlineData("-PC", false)]
    public void Computer_name(string name, bool ok) => Assert.Equal(ok, AccountNames.ComputerNameProblems(name).Count == 0);

    [Theory]
    [InlineData("example.local", true)]
    [InlineData("corp", false)]
    [InlineData("-a.local", false)]
    [InlineData("", false)]
    public void Dns_domain(string domain, bool ok) => Assert.Equal(ok, AccountNames.IsValidDnsDomain(domain));
}

public class PatternTemplateTests
{
    [Fact]
    public void Tokens_and_literal_braces()
    {
        Assert.Equal(["a", "b"], PatternTemplate.Tokens("x{a}{{y}}{b}"));
        Assert.Equal("x1{y}2", PatternTemplate.Render("x{a}{{y}}{b}", t => t == "a" ? "1" : "2"));
    }

    [Theory]
    [InlineData("{a")]
    [InlineData("a}")]
    [InlineData("{}")]
    public void Malformed(string pattern) => Assert.Throws<FormatException>(() => PatternTemplate.Tokens(pattern));

    [Fact]
    public void Unknown_token_is_invalid()
    {
        Assert.False(PatternTemplate.IsValid("{given1}.{nope}", UserNameBuilder.MailTokens, out var problem));
        Assert.Contains("nope", problem);
    }
}
