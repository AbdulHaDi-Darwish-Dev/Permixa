namespace Permixa.Application.Authentication.Abstractions;

/// <summary>
/// Cryptographically secure refresh-token raw value generation and hashing.
/// </summary>
public interface IRefreshTokenCrypto
{
    /// <summary>
    /// Creates a Base64Url-encoded raw refresh token from 32 cryptographically random bytes.
    /// </summary>
    string GenerateRawToken();

    /// <summary>
    /// SHA-256 hash of the raw token as lowercase hexadecimal.
    /// </summary>
    string HashToken(string rawToken);
}
