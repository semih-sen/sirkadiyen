using System.Net;
using System.Net.Sockets;

namespace Sirkadiyen.Application.Auditing;

/// <summary>Reduces a client IP to a coarse, privacy-preserving form for default display.</summary>
public static class AuditIp
{
    /// <summary>
    /// Returns the address with its host bits cleared — the last octet for IPv4, the low 80 bits
    /// (keeping the /48 prefix) for IPv6 — or <see langword="null"/> when the value is missing or
    /// not a valid address.
    /// </summary>
    public static string? Mask(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || !IPAddress.TryParse(ip.Trim(), out IPAddress? address))
        {
            return null;
        }

        byte[] bytes = address.GetAddressBytes();
        switch (address.AddressFamily)
        {
            case AddressFamily.InterNetwork:
                bytes[3] = 0;
                return new IPAddress(bytes).ToString();

            case AddressFamily.InterNetworkV6:
                for (int index = 6; index < bytes.Length; index++)
                {
                    bytes[index] = 0;
                }

                return new IPAddress(bytes).ToString();

            default:
                return null;
        }
    }
}
