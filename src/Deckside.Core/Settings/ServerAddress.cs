using System.Net;
using System.Net.Sockets;

namespace Deckside.Core.Settings;

/// <summary>The address of the domain controller the requests are sent to: an IP address (preferred) or a host name.</summary>
public static class ServerAddress
{
    public static bool IsIpAddress(string? value) =>
        IPAddress.TryParse(value, out var ip) && ip.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6 &&
        // IPAddress.TryParse accepts "1" or "10.1" as IPv4: require the full dotted form or an IPv6 address.
        (ip.AddressFamily == AddressFamily.InterNetworkV6 || value!.Count(c => c == '.') == 3);

    public static bool IsHostName(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 253 && !value.All(c => char.IsAsciiDigit(c) || c == '.') &&
        value.Split('.').All(l => l.Length is >= 1 and <= 63 && l.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') && l[0] != '-' && l[^1] != '-');

    public static bool IsValid(string? value) => IsIpAddress(value) || IsHostName(value);

    public static int EffectivePort(int port, bool useLdaps) => port > 0 ? port : useLdaps ? 636 : 389;
}
