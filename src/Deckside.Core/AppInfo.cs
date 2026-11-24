namespace Deckside.Core;

/// <summary>Product identity shown in "About" and written in every record.</summary>
public static class AppInfo
{
    public const string Name = "Deckside";

    /// <summary>Must match <c>VersionPrefix</c> in Directory.Build.props (a test checks it).</summary>
    public const string Version = "0.1.0";

    public const string Author = "Jose Martorell";
}
