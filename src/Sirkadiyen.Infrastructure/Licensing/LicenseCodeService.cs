using System.Security.Cryptography;
using System.Text;
using Sirkadiyen.Application.Licensing;

namespace Sirkadiyen.Infrastructure.Licensing;

/// <summary>Generates high-entropy codes and computes their keyed lookup hash.</summary>
public sealed class LicenseCodeService(LicenseCodeOptions options) : ILicenseCodeService
{
    private const string Prefix = "SRK";
    private const string LegacyPrefix = "SIRK";
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
    private const int PayloadLength = 10;
    private const int LegacyPayloadLength = 20;
    private const int GroupLength = 5;

    private readonly byte[] hashKey = ValidateOptions(options);

    public GeneratedLicenseCode Generate()
    {
        byte[] random = RandomNumberGenerator.GetBytes(PayloadLength);
        Span<char> payload = stackalloc char[PayloadLength];
        for (int index = 0; index < random.Length; index++)
        {
            payload[index] = Alphabet[random[index] & 31];
        }

        string payloadText = payload.ToString();
        string compact = Prefix + payloadText;
        string plaintext = Prefix + "-" + string.Join(
            '-',
            Enumerable.Range(0, PayloadLength / GroupLength)
                .Select(index => payloadText.Substring(index * GroupLength, GroupLength)));

        return new GeneratedLicenseCode
        {
            PlaintextCode = plaintext,
            CodeHash = HashCompact(compact),
        };
    }

    public bool TryHash(string plaintextCode, out byte[] codeHash)
    {
        codeHash = [];
        if (string.IsNullOrWhiteSpace(plaintextCode) || plaintextCode.Length > 64)
        {
            return false;
        }

        string compact = new(
            plaintextCode
                .Where(character => character is not '-' && !char.IsWhiteSpace(character))
                .Select(char.ToUpperInvariant)
                .ToArray());

        int prefixLength;
        if (compact.Length == Prefix.Length + PayloadLength
            && compact.StartsWith(Prefix, StringComparison.Ordinal))
        {
            prefixLength = Prefix.Length;
        }
        else if (compact.Length == LegacyPrefix.Length + LegacyPayloadLength
            && compact.StartsWith(LegacyPrefix, StringComparison.Ordinal))
        {
            // Codes created before ADR-054 remain redeemable. Only generation
            // moves to the shorter human-friendly format.
            prefixLength = LegacyPrefix.Length;
        }
        else
        {
            return false;
        }

        if (compact[prefixLength..].Any(character => !Alphabet.Contains(character)))
        {
            return false;
        }

        codeHash = HashCompact(compact);
        return true;
    }

    private byte[] HashCompact(string compact) =>
        HMACSHA256.HashData(hashKey, Encoding.ASCII.GetBytes(compact));

    private static byte[] ValidateOptions(LicenseCodeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.HashKey);
        if (options.HashKey.Length < 32)
        {
            throw new ArgumentException(
                "The license hash key must contain at least 32 bytes.",
                nameof(options));
        }

        return [.. options.HashKey];
    }
}
