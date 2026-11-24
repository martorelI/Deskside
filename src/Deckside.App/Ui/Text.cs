using System.Globalization;
using System.Reflection;
using Deckside.Core.Localization;

namespace Deckside.App.Ui;

/// <summary>
/// Texts of the window: <see cref="S"/> returns the Spanish version from <c>Resources/ui.es.tsv</c> (with
/// accents) when the language is Spanish. Texts of Core come translated already (<c>Translator</c>).
/// </summary>
internal static class Text
{
    private static IReadOnlyDictionary<string, string>? table;

    public static Translator Core { get; private set; } = Translator.EnglishOnly;

    public static CultureInfo Culture => Core.Culture;

    public static void Initialize(string language)
    {
        Core = new Translator(language);
        table = Core.Language == Translator.Spanish ? Load() : null;
    }

    public static string S(string text) => table is not null && table.TryGetValue(text, out var es) ? es : text;

    public static string F(string text, params object?[] args) => string.Format(Culture, S(text), args);

    internal static IReadOnlyDictionary<string, string> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Deckside.App.ui.es.tsv")
            ?? throw new InvalidOperationException("Missing ui.es.tsv");
        using var reader = new StreamReader(stream);
        return TsvTable.Parse(reader.ReadToEnd());
    }
}
