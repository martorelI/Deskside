namespace Deckside.Core.Security;

/// <summary>
/// Hides secrets (initial passwords, typed credentials) from every text that leaves the process: logs,
/// records, plan files, exports. Secrets are registered as soon as they exist and replaced by <see cref="Mask"/>.
/// </summary>
public sealed class SecretMasker
{
    public const string Mask = "********";

    private readonly object gate = new();
    private readonly List<string> secrets = [];

    public void Register(string? secret)
    {
        if (string.IsNullOrEmpty(secret))
        {
            return;
        }

        lock (gate)
        {
            if (!secrets.Contains(secret, StringComparer.Ordinal))
            {
                secrets.Add(secret);
                // Longest first, so a secret that contains another one is hidden completely.
                secrets.Sort((a, b) => b.Length.CompareTo(a.Length));
            }
        }
    }

    public string Apply(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        lock (gate)
        {
            foreach (var secret in secrets)
            {
                text = text.Replace(secret, Mask, StringComparison.Ordinal);
            }
        }

        return text;
    }

    /// <summary>Attributes whose value is never written anywhere, whatever their content.</summary>
    public static bool IsSecretAttribute(string attributeName) =>
        string.Equals(attributeName, "unicodePwd", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(attributeName, "userPassword", StringComparison.OrdinalIgnoreCase);
}
