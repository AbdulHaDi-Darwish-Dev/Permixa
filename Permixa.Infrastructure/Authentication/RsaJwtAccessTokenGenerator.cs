using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Common.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Permixa.Infrastructure.Authentication;

public sealed class PermixaJwtOptions
{
    public string? Issuer { get; set; }

    public string? Audience { get; set; }

    /// <summary>
    /// PEM-encoded RSA private key (PKCS#8 or PKCS#1). Externally supplied; never hardcoded.
    /// </summary>
    public string? PrivateKeyPem { get; set; }

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);
}

public sealed class RsaJwtAccessTokenGenerator : IAccessTokenGenerator
{
    private readonly PermixaJwtOptions _options;
    private readonly IClock _clock;
    private readonly SigningCredentials _credentials;

    public RsaJwtAccessTokenGenerator(
        IOptions<PermixaJwtOptions> options,
        IClock clock)
    {
        _options = options.Value;
        _clock = clock;

        if (string.IsNullOrWhiteSpace(_options.Issuer))
            throw new InvalidOperationException("Permixa JWT Issuer is required.");

        if (string.IsNullOrWhiteSpace(_options.Audience))
            throw new InvalidOperationException("Permixa JWT Audience is required.");

        if (string.IsNullOrWhiteSpace(_options.PrivateKeyPem))
        {
            throw new InvalidOperationException(
                "Permixa JWT PrivateKeyPem is required. Supply an RSA private key via configuration.");
        }

        using var rsa = RSA.Create();
        rsa.ImportFromPem(_options.PrivateKeyPem);

        if (rsa.KeySize < 2048)
            throw new InvalidOperationException("Permixa JWT RSA key must be at least 2048 bits.");

        // Export parameters so IdentityModel owns its key material and does not dispose a shared RSA instance.
        _credentials = new SigningCredentials(
            new RsaSecurityKey(rsa.ExportParameters(includePrivateParameters: true)),
            SecurityAlgorithms.RsaSha256);
    }

    public Task<GeneratedAccessToken> GenerateAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var now = _clock.UtcNow;
        var expires = now.Add(_options.AccessTokenLifetime);

        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString("D")),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("D")),
            new Claim(
                JwtRegisteredClaimNames.Iat,
                Epoch(now).ToString(),
                ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _credentials);

        var encoded = new JwtSecurityTokenHandler().WriteToken(token);
        return Task.FromResult(new GeneratedAccessToken(encoded, expires));
    }

    private static long Epoch(DateTime utc) =>
        new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
}
