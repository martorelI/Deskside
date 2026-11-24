namespace Deckside.Tests.Support;

internal static class Repo
{
    public static string Root { get; } = Find();

    public static string PathOf(params string[] parts) => Path.Combine([Root, .. parts]);

    private static string Find()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Deckside.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Deckside.slnx not found above " + AppContext.BaseDirectory);
    }
}
