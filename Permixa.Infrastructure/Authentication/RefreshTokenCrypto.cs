using System.Security.Cryptography;
using System.Text;
using Permixa.Application.Authentication.Abstractions;

namespace Permixa.Infrastructure.Authentication;

public sealed class RefreshTokenCrypto : IRefreshTokenCrypto
{
    public const int RawTokenByteLength = 32;

    public string GenerateRawToken()
    {
        Span<byte> bytes = stackalloc byte[RawTokenByteLength];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    public string HashToken(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> data)
    {
        var base64 = Convert.ToBase64String(data);
        return base64.TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
