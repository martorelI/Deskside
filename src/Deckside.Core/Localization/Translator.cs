using System.Globalization;
using System.Reflection;

namespace Deckside.Core.Localization;

/// <summary>
/// Translates the English texts of the product code. English is the base language: the text passed to
/// <see cref="T"/> is the key, and <c>es.tsv</c> holds its Spanish version. Unknown texts are returned as they
/// are (a test requires every text to be translated, so that only happens during development).
/// </summary>
public sealed class Translator
{
    public const string English = "en";
    public const string Spanish = "es";

    private static readonly Lazy<IReadOnlyDictionary<string, string>> SpanishTable =
        new(() => TsvTable.Parse(ReadEmbedded("Deckside.Core.es.tsv")));

    private readonly IReadOnlyDictionary<string, string>? table;

    public Translator(string language)
    {
        Language = IsSupported(language) ? language.ToLowerInvariant() : English;
        table = Language == Spanish ? SpanishTable.Value : null;
    }

    public static Translator EnglishOnly { get; } = new(English);

    public string Language { get; }

    /// <summary>Culture used to format numbers and dates shown to the operator (never for the directory).</summary>
    public CultureInfo Culture => Language == Spanish ? CultureInfo.GetCultureInfo("es-MX") : CultureInfo.GetCultureInfo("en-US");

    public static bool IsSupported(string? language) =>
        string.Equals(language, English, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(language, Spanish, StringComparison.OrdinalIgnoreCase);

    /// <summary>Translates <paramref name="text"/> and, if arguments are given, formats it with them.</summary>
    public string T(string text, params object?[] args)
    {
        var translated = table is not null && table.TryGetValue(text, out var es) ? es : text;
        return args.Length == 0 ? translated : string.Format(Culture, translated, args);
    }

    internal static IReadOnlyDictionary<string, string> SpanishEntries => SpanishTable.Value;

    private static string ReadEmbedded(string name)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
            ?? throw new InvalidOperationException("Missing embedded resource " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

/// <summary>
/// Format of the translation files: one entry per line, <c>English&lt;TAB&gt;Spanish</c>. Lines that start with
/// <c>#</c> and blank lines are ignored. <c>\t</c>, <c>\n</c> and <c>\\</c> are escapes. Duplicated keys are an error.
/// </summary>
public static class TsvTable
{
    public static IReadOnlyDictionary<string, string> Parse(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        var lineNumber = 0;
        foreach (var rawLine in content.Split('\n'))
        {
            lineNumber++;
            var line = rawLine.TrimEnd('\r');
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var tab = line.IndexOf('\t', StringComparison.Ordinal);
            if (tab <= 0 || tab == line.Length - 1 || line.IndexOf('\t', tab + 1) >= 0)
            {
                throw new FormatException(string.Format(CultureInfo.InvariantCulture, "Line {0}: expected 'English<TAB>Spanish'.", lineNumber));
            }

            var key = Unescape(line[..tab]);
            if (!result.TryAdd(key, Unescape(line[(tab + 1)..])))
            {
                throw new FormatException(string.Format(CultureInfo.InvariantCulture, "Line {0}: duplicated text '{1}'.", lineNumber, key));
            }
        }

        return result;
    }

    public static string Unescape(string value)
    {
        if (!value.Contains('\\', StringComparison.Ordinal))
        {
            return value;
        }

        var sb = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                var next = value[++i];
                sb.Append(next switch { 't' => '\t', 'n' => '\n', '\\' => '\\', _ => next });
            }
            else
            {
                sb.Append(value[i]);
            }
        }

        return sb.ToString();
    }
}
