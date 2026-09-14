using System.Security.Cryptography;

namespace Permixa.AspNetCore.Authentication;

/// <summary>
/// Options for incoming JWT Bearer validation. Distinct from JWT issuance options.
/// </summary>
public sealed class PermixaJwtBearerOptions
{
    public string? Issuer { get; set; }

    public string? Audience { get; set; }

    /// <summary>
    /// PEM-encoded RSA public key used for signature validation.
    /// </summary>
    public string? PublicKeyPem { get; set; }

    /// <summary>
    /// Allowed clock skew for lifetime validation. Default 30 seconds.
    /// </summary>
    public TimeSpan ClockSkew { get; set; } = TimeSpan.FromSeconds(30);
}

internal static class RsaPublicKeyLoader
{
    public static RSA LoadPublicKey(string publicKeyPem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(publicKeyPem);

        if (rsa.KeySize < 2048)
        {
            rsa.Dispose();
            throw new InvalidOperationException(
                "Permixa JWT Bearer PublicKeyPem must be at least 2048 bits.");
        }

        return rsa;
    }
}
