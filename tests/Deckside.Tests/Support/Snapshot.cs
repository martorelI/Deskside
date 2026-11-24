using System.Text;

namespace Deckside.Tests.Support;

/// <summary>
/// Compares a text with <c>tests/snapshots/&lt;name&gt;</c>. With <c>DECKSIDE_UPDATE_SNAPSHOTS=1</c> the file is
/// (re)written instead: only when the change is intentional, and review the diff.
/// </summary>
internal static class Snapshot
{
    public static void Match(string name, string actual)
    {
        actual = actual.Replace("\r\n", "\n", StringComparison.Ordinal);
        var path = Repo.PathOf("tests", "snapshots", name);
        if (Environment.GetEnvironmentVariable("DECKSIDE_UPDATE_SNAPSHOTS") == "1")
        {
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, actual, new UTF8Encoding(false));
            return;
        }

        if (!File.Exists(path))
        {
            var outPath = Repo.PathOf("tests", "out", name);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, actual, new UTF8Encoding(false));
            Assert.Fail($"Snapshot {name} does not exist. The result was written to tests/out/{name}; run with DECKSIDE_UPDATE_SNAPSHOTS=1 to accept it.");
        }

        var expected = File.ReadAllText(path, Encoding.UTF8);
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
        {
            var outPath = Repo.PathOf("tests", "out", name);
            System.IO.Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            File.WriteAllText(outPath, actual, new UTF8Encoding(false));
            Assert.Equal(expected, actual);
        }
    }
}
