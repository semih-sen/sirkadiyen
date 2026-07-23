namespace Sirkadiyen.Infrastructure.Licensing;

public sealed record LicenseCodeOptions
{
    public required byte[] HashKey { get; init; }

    public static LicenseCodeOptions FromBase64(string encodedHashKey)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(encodedHashKey);

        byte[] key;
        try
        {
            key = Convert.FromBase64String(encodedHashKey.Trim());
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException(
                "SIRKADIYEN_LICENSING__HASH_KEY must be a Base64 value.",
                exception);
        }

        if (key.Length < 32)
        {
            throw new InvalidOperationException(
                "SIRKADIYEN_LICENSING__HASH_KEY must decode to at least 32 bytes.");
        }

        return new LicenseCodeOptions { HashKey = key };
    }
}
