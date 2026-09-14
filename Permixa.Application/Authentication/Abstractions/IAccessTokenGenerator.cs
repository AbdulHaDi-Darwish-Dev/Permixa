namespace Permixa.Application.Authentication.Abstractions;

public interface IAccessTokenGenerator
{
    Task<GeneratedAccessToken> GenerateAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed record GeneratedAccessToken(
    string AccessToken,
    DateTime ExpiresAtUtc);
