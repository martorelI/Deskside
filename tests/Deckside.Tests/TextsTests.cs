using System.Text.RegularExpressions;
using System.Xml.Linq;
using Deckside.Core;
using Deckside.Core.Localization;
using Deckside.Tests.Support;

namespace Deckside.Tests;

public partial class CoreTextsTests
{
    [GeneratedRegex("""\bT\("((?:[^"\\]|\\.)*)"[,)]""")]
    private static partial Regex TCall();

    [GeneratedRegex("""\bT\(\$""")]
    private static partial Regex InterpolatedTCall();

    private static IEnumerable<string> CoreSources() =>
        System.IO.Directory.EnumerateFiles(Repo.PathOf("src", "Deckside.Core"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

    private static HashSet<string> UsedTexts() =>
        [.. CoreSources().SelectMany(f => TCall().Matches(File.ReadAllText(f)).Select(m => Regex.Unescape(m.Groups[1].Value)))];

    [Fact]
    public void Every_text_has_a_spanish_translation()
    {
        var missing = UsedTexts().Where(t => !Translator.SpanishEntries.ContainsKey(t)).Order().ToList();
        Assert.True(missing.Count == 0, "Missing in es.tsv:\n" + string.Join("\n", missing));
    }

    [Fact]
    public void No_unused_translations()
    {
        var used = UsedTexts();
        var unused = Translator.SpanishEntries.Keys.Where(k => !used.Contains(k)).Order().ToList();
        Assert.True(unused.Count == 0, "Unused in es.tsv:\n" + string.Join("\n", unused));
    }

    [Fact]
    public void Texts_are_literals_and_placeholders_match()
    {
        Assert.DoesNotContain(CoreSources(), f => InterpolatedTCall().IsMatch(File.ReadAllText(f)));
        foreach (var (en, es) in Translator.SpanishEntries)
        {
            var a = Regex.Matches(en, @"\{\d+\}").Select(m => m.Value).Order();
            var b = Regex.Matches(es, @"\{\d+\}").Select(m => m.Value).Order();
            Assert.True(a.SequenceEqual(b), "Placeholders differ: " + en);
        }
    }

    [Fact]
    public void Translator_formats_and_falls_back_to_english()
    {
        Assert.Equal("Usuarios", new Translator("ES").T("Users"));
        Assert.Equal("Users", new Translator("fr").T("Users"));
        Assert.Equal("Ya existe un objeto con el DN X", new Translator("es").T("An object already exists with the DN {0}", "X"));
    }

    [Fact]
    public void Tsv_format_errors()
    {
        Assert.Throws<FormatException>(() => TsvTable.Parse("a\tb\na\tc\n"));
        Assert.Throws<FormatException>(() => TsvTable.Parse("no tab\n"));
        Assert.Equal("x\ty", TsvTable.Parse("# c\n\nk\\tk\tx\\ty\n")["k\tk"]);
    }
}

public class VersionTests
{
    [Fact]
    public void Props_version_matches_app_info()
    {
        var props = XDocument.Load(Repo.PathOf("Directory.Build.props"));
        var version = props.Descendants("VersionPrefix").Single().Value;
        Assert.Equal(AppInfo.Version, version);
    }
}
