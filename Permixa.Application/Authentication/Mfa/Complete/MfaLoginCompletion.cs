using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Mfa.Enable;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authentication;
using Microsoft.Extensions.Options;

namespace Permixa.Application.Authentication.Mfa.Complete;

public sealed class MfaLoginCompletion
{
    private readonly IMfaLoginChallengeRepository _challenges;
    private readonly IRefreshTokenCrypto _crypto;
    private readonly IIdentityUserReader _users;
    private readonly IIdentityMfa _mfa;
    private readonly IAuthenticationTokenService _tokens;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly PermixaAuthenticationOptions _authentication;
    private readonly PermixaMfaOptions _mfaOptions;

    public MfaLoginCompletion(
        IMfaLoginChallengeRepository challenges,
        IRefreshTokenCrypto crypto,
        IIdentityUserReader users,
        IIdentityMfa mfa,
        IAuthenticationTokenService tokens,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOptions<PermixaAuthenticationOptions> authentication,
        IOptions<PermixaMfaOptions> mfaOptions)
    {
        _challenges = challenges;
        _crypto = crypto;
        _users = users;
        _mfa = mfa;
        _tokens = tokens;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _authentication = authentication.Value;
        _mfaOptions = mfaOptions.Value;
    }

    public async Task<Result<MfaLoginChallenge>> ResolveActiveAsync(
        string? rawProof,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(rawProof))
            return Result.Failure<MfaLoginChallenge>(MfaErrors.ChallengeInvalid);

        var hash = _crypto.HashToken(rawProof.Trim());
        var challenge = await _challenges.GetByProofHashAsync(hash, cancellationToken);
        if (challenge is null || challenge.IsConsumed || challenge.IsExpired(_clock.UtcNow))
            return Result.Failure<MfaLoginChallenge>(MfaErrors.ChallengeInvalid);

        if (challenge.AttemptCount >= _mfaOptions.MaxAttempts)
            return Result.Failure<MfaLoginChallenge>(MfaErrors.AttemptsExceeded);

        return Result.Success(challenge);
    }

    public async Task<Result?> EnsureIssuableAsync(
        MfaLoginChallenge challenge,
        CancellationToken cancellationToken)
    {
        var gate = await _users.GetMfaLoginGateAsync(challenge.UserId, _clock.UtcNow, cancellationToken);
        if (gate is null || gate.IsDisabled)
            return Result.Failure(AuthenticationErrors.InvalidCredentials);

        if (gate.IsLocked)
            return Result.Failure(AuthenticationErrors.LockedOut);

        if (_authentication.RequireConfirmedEmail && !gate.EmailConfirmed)
            return Result.Failure(AuthenticationErrors.EmailNotConfirmed);

        if (!gate.TwoFactorEnabled)
            return Result.Failure(MfaErrors.NotEnabled);

        return null;
    }

    public async Task<Result<AuthenticationResult>> RedeemConsumeAndIssueAsync(
        MfaLoginChallenge challenge,
        string recoveryCode,
        CancellationToken cancellationToken)
    {
        Result<AuthenticationResult>? issued = null;
        var redeemed = false;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            redeemed = await _mfa.RedeemRecoveryCodeAsync(challenge.UserId, recoveryCode, ct);
            if (!redeemed)
                return;

            if (await _challenges.ConsumeUnconsumedAsync(challenge.Id, _clock.UtcNow, ct) != 1)
                return;

            issued = await _tokens.IssueAsync(challenge.UserId, ct);
        }, cancellationToken);

        if (!redeemed)
            return Result.Failure<AuthenticationResult>(MfaErrors.CodeInvalid);

        return issued ?? Result.Failure<AuthenticationResult>(MfaErrors.ChallengeInvalid);
    }

    public async Task<Result<AuthenticationResult>> ConsumeAndIssueAsync(
        MfaLoginChallenge challenge,
        CancellationToken cancellationToken)
    {
        Result<AuthenticationResult>? issued = null;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            if (await _challenges.ConsumeUnconsumedAsync(challenge.Id, _clock.UtcNow, ct) != 1)
                return;

            issued = await _tokens.IssueAsync(challenge.UserId, ct);
        }, cancellationToken);

        return issued ?? Result.Failure<AuthenticationResult>(MfaErrors.ChallengeInvalid);
    }

    public async Task<Result> RegisterInvalidCodeAsync(
        MfaLoginChallenge challenge,
        CancellationToken cancellationToken)
    {
        var attempts = await _challenges.IncrementAttemptsAsync(challenge.Id, cancellationToken);
        if (attempts >= _mfaOptions.MaxAttempts)
        {
            await _challenges.ConsumeUnconsumedAsync(challenge.Id, _clock.UtcNow, cancellationToken);
            return Result.Failure(MfaErrors.AttemptsExceeded);
        }

        return Result.Failure(MfaErrors.CodeInvalid);
    }

    public static string NormalizeTotp(string? code) =>
        EnableAuthenticatorMfaUseCase.NormalizeTotp(code);
}
