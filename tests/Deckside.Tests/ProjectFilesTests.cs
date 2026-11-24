using System.Text.RegularExpressions;
using System.Xml.Linq;
using Deckside.Tests.Support;

namespace Deckside.Tests;

public partial class ProjectFilesTests
{
    // name.es.tsv, name.en-US.json...: MSBuild takes the middle part as a culture.
    [GeneratedRegex(@"\.[a-z]{2,3}(-[A-Za-z]{2,4})?\.[^.\\/]+$")]
    private static partial Regex CultureLikeName();

    /// <summary>
    /// An embedded file whose name looks like <c>name.culture.ext</c> goes to a satellite assembly
    /// (<c>es/Deckside.resources.dll</c>) unless it says <c>WithCulture="false"</c>, and then
    /// <c>GetManifestResourceStream</c> returns null at run time.
    /// </summary>
    [Fact]
    public void Embedded_files_are_never_treated_as_satellite_resources()
    {
        var projects = System.IO.Directory.EnumerateFiles(Repo.PathOf("src"), "*.csproj", SearchOption.AllDirectories);
        var resources = projects.SelectMany(p => XDocument.Load(p).Descendants("EmbeddedResource").Select(e => (Project: Path.GetFileName(p), Element: e))).ToList();
        Assert.NotEmpty(resources);
        foreach (var (project, e) in resources)
        {
            var include = (string?)e.Attribute("Include") ?? "";
            Assert.False(string.IsNullOrEmpty((string?)e.Attribute("LogicalName")), project + ": " + include + " needs a LogicalName");
            if (CultureLikeName().IsMatch(include) || include.EndsWith("es.tsv", StringComparison.OrdinalIgnoreCase))
            {
                Assert.True(string.Equals((string?)e.Attribute("WithCulture"), "false", StringComparison.OrdinalIgnoreCase),
                    project + ": " + include + " needs WithCulture=\"false\"");
            }
        }
    }
}
