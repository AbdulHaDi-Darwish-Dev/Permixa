using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.IdentityModel.Tokens;

namespace Permixa.AspNetCore.Tests;

internal static class TestRsaKeys
{
    private static readonly Lazy<(string PrivatePem, string PublicPem)> Keys = new(Create);

    public static string PrivateKeyPem => Keys.Value.PrivatePem;

    public static string PublicKeyPem => Keys.Value.PublicPem;

    public const string Issuer = "permixa-aspnet-tests";
    public const string Audience = "permixa-aspnet-tests-api";

    private static (string PrivatePem, string PublicPem) Create()
    {
        using var rsa = RSA.Create(2048);
        return (rsa.ExportPkcs8PrivateKeyPem(), rsa.ExportSubjectPublicKeyInfoPem());
    }

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
        // JwtSecurityToken requires expires > notBefore; for expired tokens move nbf earlier.
        var notBefore = lifetimeValue < TimeSpan.Zero
            ? expires.AddMinutes(-5)
            : now;
        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new(JwtRegisteredClaimNames.Iat, new DateTimeOffset(now).ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        if (includeSub)
        {
            claims.Add(new Claim(
                JwtRegisteredClaimNames.Sub,
                rawSub ?? userId!.Value.ToString("D")));
        }

        var token = new JwtSecurityToken(
            issuer: issuer ?? Issuer,
            audience: audience ?? Audience,
            claims: claims,
            notBefore: notBefore,
            expires: expires,
            signingCredentials: new SigningCredentials(
                new RsaSecurityKey(rsa.ExportParameters(true)),
                SecurityAlgorithms.RsaSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
