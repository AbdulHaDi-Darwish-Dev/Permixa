using System.Security.Cryptography;

namespace Permixa.IntegrationTests.Support;

internal static class TestKeys
{
    private static readonly Lazy<(string PrivatePem, string PublicPem)> Keys = new(Create);

    public static string PrivateKeyPem => Keys.Value.PrivatePem;

    public static string PublicKeyPem => Keys.Value.PublicPem;

    public const string Issuer = "permixa-integration-tests";
    public const string Audience = "permixa-integration-tests-api";
    public const string FakeResendApiKey = "re_test_not_a_real_key";
    public const string OwnerEmail = "owner@permixa.test";
    public const string OwnerUserName = "owner";
    public const string OwnerPassword = "OwnerPass1!";
    public const string UserPassword = "Passw0rd!";
    public const string EmailConfirmationUrlTemplate =
        "https://permixa.test/confirm?challengeId={challengeId}&token={token}";
    public const string PasswordResetUrlTemplate =
        "https://permixa.test/reset?challengeId={challengeId}&token={token}";

    public static string CreateAccessToken(
        Guid? userId,
        TimeSpan? lifetime = null,
        string? issuer = null,
        string? audience = null,
        string? privatePem = null,
        bool includeSub = true,
        string? rawSub = null)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privatePem ?? PrivateKeyPem);

        var now = DateTime.UtcNow;
        var lifetimeValue = lifetime ?? TimeSpan.FromMinutes(15);
        var expires = now.Add(lifetimeValue);
        var notBefore = lifetimeValue < TimeSpan.Zero ? expires.AddMinutes(-5) : now;

        var claims = new List<System.Security.Claims.Claim>
        {
            new(System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Iat,
                new DateTimeOffset(now).ToUnixTimeSeconds().ToString(),
                System.Security.Claims.ClaimValueTypes.Integer64)
        };

        if (includeSub)
        {
            claims.Add(new System.Security.Claims.Claim(
                System.IdentityModel.Tokens.Jwt.JwtRegisteredClaimNames.Sub,
                rawSub ?? userId!.Value.ToString("D")));
        }

        var token = new System.IdentityModel.Tokens.Jwt.JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: new Microsoft.IdentityModel.Tokens.SigningCredentials(
                new Microsoft.IdentityModel.Tokens.RsaSecurityKey(rsa.ExportParameters(true)),
                Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256));

        return new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().WriteToken(token);
    }

    private static (string PrivatePem, string PublicPem) Create()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }
}
