namespace Permixa.Application.Authentication.Models;

public sealed class RegisterRequest
{
    public required string UserName { get; init; }

    public required string Email { get; init; }

    public required string Password { get; init; }
}

public sealed class RegisterResult
{
    public required Guid UserId { get; init; }

    public required string UserName { get; init; }

    public required string Email { get; init; }

    public required bool EmailConfirmed { get; init; }
}

public sealed class LoginRequest
{
    public required string EmailOrUserName { get; init; }

    public required string Password { get; init; }
}

public sealed class RefreshTokenRequest
{
    public required string RefreshToken { get; init; }
}

public sealed class RevokeRefreshTokenRequest
{
    public required string RefreshToken { get; init; }
}

public sealed class AuthenticationResult
{
    public required Guid UserId { get; init; }

    public required string AccessToken { get; init; }

    public required DateTime AccessTokenExpiresAtUtc { get; init; }

    public required string RefreshToken { get; init; }

    public required DateTime RefreshTokenExpiresAtUtc { get; init; }
}
