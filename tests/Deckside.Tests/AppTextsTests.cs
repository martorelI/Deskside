using System.Text.RegularExpressions;
using Deckside.Core.Localization;
using Deckside.Tests.Support;

namespace Deckside.Tests;

/// <summary>The window texts (<c>S("...")</c> / <c>F("...")</c>) and <c>ui.es.tsv</c> match exactly. Read as files: the App targets Windows.</summary>
public partial class AppTextsTests
{
    [GeneratedRegex("""\b[SF]\("((?:[^"\\]|\\.)*)"[,)]""")]
    private static partial Regex Call();

    private static IReadOnlyDictionary<string, string> Table() =>
        TsvTable.Parse(File.ReadAllText(Repo.PathOf("src", "Deckside.App", "Resources", "ui.es.tsv")));

    private static HashSet<string> Used() =>
        [.. System.IO.Directory.EnumerateFiles(Repo.PathOf("src", "Deckside.App"), "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .SelectMany(f => Call().Matches(File.ReadAllText(f)).Select(m => Regex.Unescape(m.Groups[1].Value)))];

    [Fact]
    public void Every_window_text_is_translated_and_none_is_unused()
    {
        var table = Table();
        var used = Used();
        Assert.Empty(used.Where(t => !table.ContainsKey(t)).Order());
        Assert.Empty(table.Keys.Where(k => !used.Contains(k)).Order());
    }

    [Fact]
    public void Placeholders_match()
    {
        foreach (var (en, es) in Table())
        {
            Assert.True(Regex.Matches(en, @"\{\d+\}").Select(m => m.Value).Order().SequenceEqual(Regex.Matches(es, @"\{\d+\}").Select(m => m.Value).Order()), en);
        }
    }
}
