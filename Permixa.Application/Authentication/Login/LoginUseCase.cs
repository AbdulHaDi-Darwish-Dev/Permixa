using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Mfa;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Common.Results;
using Microsoft.Extensions.Options;

namespace Permixa.Application.Authentication.Login;

public sealed class LoginUseCase
{
    private readonly IIdentityAuthenticator _authenticator;
    private readonly IAuthenticationTokenService _tokens;
    private readonly MfaLoginChallengeIssuer _mfaChallenges;
    private readonly PermixaAuthenticationOptions _options;

    public LoginUseCase(
        IIdentityAuthenticator authenticator,
        IAuthenticationTokenService tokens,
        MfaLoginChallengeIssuer mfaChallenges,
        IOptions<PermixaAuthenticationOptions> options)
    {
        _authenticator = authenticator;
        _tokens = tokens;
        _mfaChallenges = mfaChallenges;
        _options = options.Value;
    }

    public async Task<Result<LoginResult>> ExecuteAsync(
        LoginRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.EmailOrUserName)
            || string.IsNullOrWhiteSpace(request.Password))
        {
            return Result.Failure<LoginResult>(AuthenticationErrors.InvalidCredentials);
        }

        var auth = await _authenticator.AuthenticateAsync(
            request.EmailOrUserName.Trim(),
            request.Password,
            cancellationToken);

        if (auth.IsLockedOut)
            return Result.Failure<LoginResult>(AuthenticationErrors.LockedOut);

        if (!auth.Succeeded || auth.UserId is null)
            return Result.Failure<LoginResult>(AuthenticationErrors.InvalidCredentials);

        if (_options.RequireConfirmedEmail && !auth.EmailConfirmed)
            return Result.Failure<LoginResult>(AuthenticationErrors.EmailNotConfirmed);

        if (auth.TwoFactorEnabled)
        {
            var pending = await _mfaChallenges.IssueAsync(auth.UserId.Value, cancellationToken);
            return Result.Success(LoginResult.MfaRequired(pending.Proof, pending.ExpiresAtUtc));
        }

        var issued = await _tokens.IssueAsync(auth.UserId.Value, cancellationToken);
        if (issued.IsFailure)
            return Result.Failure<LoginResult>(issued.Error!);

        return Result.Success(LoginResult.Authenticated(issued.Value));
    }
}
