namespace Deckside.Core.Naming;

/// <summary>A name could not be built. <see cref="Code"/> is an internal English code (never translated text).</summary>
public sealed class NamingException(string code, string detail) : Exception(code + ": " + detail)
{
    public string Code { get; } = code;

    public string Detail { get; } = detail;
}
