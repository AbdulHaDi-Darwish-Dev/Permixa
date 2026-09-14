using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Common;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authentication;
using Microsoft.Extensions.Options;

namespace Permixa.Application.Authentication;

/// <summary>
/// Configurable authentication lifetimes (refresh). Access-token lifetime lives with JWT options in Infrastructure.
/// </summary>
public sealed class PermixaAuthenticationOptions
{
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// When true, successful credential validation still fails login if EmailConfirmed is false.
    /// Default false preserves Phase 4 login behavior.
    /// </summary>
    public bool RequireConfirmedEmail { get; set; }
}

/// <summary>
/// Issues access + refresh token pairs and performs rotation/revocation orchestration helpers.
/// </summary>
public interface IAuthenticationTokenService
{
    Task<Result<Models.AuthenticationResult>> IssueAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<Result<Models.AuthenticationResult>> RotateAsync(
        string rawRefreshToken,
        CancellationToken cancellationToken = default);

    Task<Result> RevokeAsync(
        string rawRefreshToken,
        CancellationToken cancellationToken = default);
}

public sealed class AuthenticationTokenService : IAuthenticationTokenService
{
    private readonly IAccessTokenGenerator _accessTokens;
    private readonly IRefreshTokenCrypto _refreshCrypto;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IIdentityUserReader _users;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly PermixaAuthenticationOptions _options;

    public AuthenticationTokenService(
        IAccessTokenGenerator accessTokens,
        IRefreshTokenCrypto refreshCrypto,
        IRefreshTokenRepository refreshTokens,
        IIdentityUserReader users,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOptions<PermixaAuthenticationOptions> options)
    {
        _accessTokens = accessTokens;
        _refreshCrypto = refreshCrypto;
        _refreshTokens = refreshTokens;
        _users = users;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<Models.AuthenticationResult>> IssueAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var access = await _accessTokens.GenerateAsync(userId, cancellationToken);
        var raw = _refreshCrypto.GenerateRawToken();
        var hash = _refreshCrypto.HashToken(raw);
        var now = _clock.UtcNow;
        var expires = now.Add(_options.RefreshTokenLifetime);

        var entity = RefreshToken.Create(
            userId,
            hash,
            expires,
            familyId: Guid.NewGuid(),
            createdAtUtc: now);

        await _refreshTokens.AddAsync(entity, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(new Models.AuthenticationResult
        {
            UserId = userId,
            AccessToken = access.AccessToken,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = raw,
            RefreshTokenExpiresAtUtc = expires
        });
    }

    public async Task<Result<Models.AuthenticationResult>> RotateAsync(
        string rawRefreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return Result.Failure<Models.AuthenticationResult>(AuthenticationErrors.InvalidRefreshToken);

        var hash = _refreshCrypto.HashToken(rawRefreshToken);
        var existing = await _refreshTokens.GetByTokenHashAsync(hash, cancellationToken);

        if (existing is null)
            return Result.Failure<Models.AuthenticationResult>(AuthenticationErrors.InvalidRefreshToken);

        var now = _clock.UtcNow;

        if (existing.WasReplaced)
        {
            await _refreshTokens.RevokeFamilyForUserAsync(
                existing.UserId, existing.FamilyId, now, cancellationToken);
            return Result.Failure<Models.AuthenticationResult>(AuthenticationErrors.RefreshTokenReuseDetected);
        }

        if (existing.IsRevoked)
            return Result.Failure<Models.AuthenticationResult>(AuthenticationErrors.RefreshTokenRevoked);

        if (existing.IsExpired(now))
            return Result.Failure<Models.AuthenticationResult>(AuthenticationErrors.RefreshTokenExpired);

        var account = await _users.GetAccountStateAsync(existing.UserId, now, cancellationToken);
        if (account is null || account.IsDisabled || account.IsLocked)
            return Result.Failure<Models.AuthenticationResult>(AuthenticationErrors.InvalidRefreshToken);

        var access = await _accessTokens.GenerateAsync(existing.UserId, cancellationToken);
        var raw = _refreshCrypto.GenerateRawToken();
        var newHash = _refreshCrypto.HashToken(raw);
        var newExpires = now.Add(_options.RefreshTokenLifetime);

        var replacement = RefreshToken.Create(
            existing.UserId,
            newHash,
            newExpires,
            existing.FamilyId,
            createdAtUtc: now);

        existing.Revoke(now, replacement.Id);
        await _refreshTokens.AddAsync(replacement, cancellationToken);

        try
        {
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (ConcurrencyConflictException)
        {
            return Result.Failure<Models.AuthenticationResult>(AuthenticationErrors.InvalidRefreshToken);
        }

        return Result.Success(new Models.AuthenticationResult
        {
            UserId = existing.UserId,
            AccessToken = access.AccessToken,
            AccessTokenExpiresAtUtc = access.ExpiresAtUtc,
            RefreshToken = raw,
            RefreshTokenExpiresAtUtc = newExpires
        });
    }

    public async Task<Result> RevokeAsync(
        string rawRefreshToken,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawRefreshToken))
            return Result.Failure(AuthenticationErrors.InvalidRefreshToken);

        var hash = _refreshCrypto.HashToken(rawRefreshToken);
        var existing = await _refreshTokens.GetByTokenHashAsync(hash, cancellationToken);

        if (existing is null)
            return Result.Failure(AuthenticationErrors.InvalidRefreshToken);

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var now = _clock.UtcNow;
            await _refreshTokens.RevokeFamilyForUserAsync(
                existing.UserId, existing.FamilyId, now, ct);
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Sessions.Logout,
                    now,
                    actorUserId: existing.UserId,
                    targetUserId: existing.UserId,
                    metadata: new Dictionary<string, string>
                    {
                        ["FamilyId"] = existing.FamilyId.ToString()
                    }),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return Result.Success();
    }
}
