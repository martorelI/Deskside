namespace Deckside.Core.Naming;

/// <summary>
/// Name of a person as typed in the form, cleaned of extra spaces. Two surnames as used in Spanish-speaking
/// countries (the second is optional). Compound names stay whole: <c>De la Cruz</c> is one surname.
/// </summary>
public sealed record PersonName
{
    public PersonName(string givenNames, string surname1, string? surname2 = null)
    {
        GivenNames = NameNormalizer.Clean(givenNames);
        Surname1 = NameNormalizer.Clean(surname1);
        Surname2 = NameNormalizer.Clean(surname2);
    }

    public string GivenNames { get; }

    public string Surname1 { get; }

    public string Surname2 { get; }

    public string Surnames => NameNormalizer.Clean(Surname1 + " " + Surname2);

    public string FullName => NameNormalizer.Clean(GivenNames + " " + Surnames);

    /// <summary>Tokens for the e-mail / account patterns (normalized ASCII).</summary>
    public static readonly IReadOnlyList<string> AsciiTokens = ["given", "given1", "g", "surname1", "surname2", "s1", "s2"];

    /// <summary>Tokens for CN / displayName / sn patterns (as typed).</summary>
    public static readonly IReadOnlyList<string> DisplayTokens = ["Given", "Given1", "Surname1", "Surname2", "Surnames"];

    public string? ResolveAscii(string token) => token switch
    {
        "given" => NameNormalizer.ToAsciiLower(GivenNames),
        "given1" => NameNormalizer.ToAsciiLower(NameNormalizer.FirstWord(GivenNames)),
        "g" => NameNormalizer.Initial(GivenNames),
        "surname1" => NameNormalizer.ToAsciiLower(Surname1),
        "surname2" => NameNormalizer.ToAsciiLower(Surname2),
        "s1" => NameNormalizer.Initial(Surname1),
        "s2" => NameNormalizer.Initial(Surname2),
        _ => null,
    };

    public string? ResolveDisplay(string token) => token switch
    {
        "Given" => GivenNames,
        "Given1" => NameNormalizer.FirstWord(GivenNames),
        "Surname1" => Surname1,
        "Surname2" => Surname2,
        "Surnames" => Surnames,
        _ => null,
    };
}
