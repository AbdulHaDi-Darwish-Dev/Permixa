using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Microsoft.Extensions.Options;

namespace Permixa.Application.Authentication.Mfa.Enable;

public sealed class EnableAuthenticatorMfaUseCase
{
    private readonly IIdentityMfa _mfa;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly PermixaMfaOptions _options;

    public EnableAuthenticatorMfaUseCase(
        IIdentityMfa mfa,
        IRefreshTokenRepository refreshTokens,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOptions<PermixaMfaOptions> options)
    {
        _mfa = mfa;
        _refreshTokens = refreshTokens;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<EnableAuthenticatorMfaResult>> ExecuteAsync(
        EnableAuthenticatorMfaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty)
            return Result.Failure<EnableAuthenticatorMfaResult>(AuthorizationErrors.UserNotFound);

        var status = await _mfa.GetStatusAsync(command.UserId, cancellationToken);
        if (status is null)
            return Result.Failure<EnableAuthenticatorMfaResult>(AuthorizationErrors.UserNotFound);

        if (status.IsEnabled)
            return Result.Failure<EnableAuthenticatorMfaResult>(MfaErrors.AlreadyEnabled);

        if (!status.HasAuthenticatorKey)
            return Result.Failure<EnableAuthenticatorMfaResult>(MfaErrors.AuthenticatorNotConfigured);

        var code = NormalizeTotp(command.TotpCode);
        if (string.IsNullOrWhiteSpace(code)
            || !await _mfa.VerifyAuthenticatorCodeAsync(command.UserId, code, cancellationToken))
        {
            return Result.Failure<EnableAuthenticatorMfaResult>(MfaErrors.CodeInvalid);
        }

        IReadOnlyList<string>? codes = null;
        var outcome = EnableOutcome.Failed;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var enabled = await _mfa.EnableAsync(command.UserId, _options.RecoveryCodeCount, ct);
            if (enabled.AlreadyEnabled)
            {
                outcome = EnableOutcome.AlreadyEnabled;
                return;
            }

            if (!enabled.Succeeded)
                return;

            var now = _clock.UtcNow;
            await _refreshTokens.RevokeAllForUserAsync(command.UserId, now, ct);
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Mfa.Enabled,
                    now,
                    actorUserId: command.UserId,
                    targetUserId: command.UserId),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);
            codes = enabled.RecoveryCodes;
            outcome = EnableOutcome.Succeeded;
        }, cancellationToken);

        return outcome switch
        {
            EnableOutcome.Succeeded => Result.Success(new EnableAuthenticatorMfaResult(codes!)),
            EnableOutcome.AlreadyEnabled =>
                Result.Failure<EnableAuthenticatorMfaResult>(MfaErrors.AlreadyEnabled),
            _ => Result.Failure<EnableAuthenticatorMfaResult>(ApplicationErrors.Unexpected)
        };
    }

    private enum EnableOutcome
    {
        Succeeded,
        AlreadyEnabled,
        Failed
    }

    internal static string NormalizeTotp(string? code) =>
        string.IsNullOrWhiteSpace(code)
            ? string.Empty
            : code.Replace(" ", string.Empty, StringComparison.Ordinal)
                .Replace("-", string.Empty, StringComparison.Ordinal)
                .Trim();
}
