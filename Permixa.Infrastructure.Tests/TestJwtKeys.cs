using System.Security.Cryptography;

namespace Permixa.Infrastructure.Tests;

internal static class TestJwtKeys
{
    private static readonly Lazy<string> Pem = new(CreatePem);

    public static string PrivateKeyPem => Pem.Value;

    public const string Issuer = "permixa-tests";
    public const string Audience = "permixa-tests-api";

    private static string CreatePem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportPkcs8PrivateKeyPem();
    }
}
