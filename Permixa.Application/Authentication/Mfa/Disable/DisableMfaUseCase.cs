using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authentication.Mfa.Disable;

public sealed class DisableMfaUseCase
{
    private readonly IIdentityMfa _mfa;
    private readonly IIdentityPasswordChange _passwords;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public DisableMfaUseCase(
        IIdentityMfa mfa,
        IIdentityPasswordChange passwords,
        IRefreshTokenRepository refreshTokens,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _mfa = mfa;
        _passwords = passwords;
        _refreshTokens = refreshTokens;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        DisableMfaCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty)
            return Result.Failure(AuthorizationErrors.UserNotFound);

        if (string.IsNullOrWhiteSpace(command.CurrentPassword))
            return Result.Failure(AuthenticationErrors.CurrentPasswordInvalid);

        var status = await _mfa.GetStatusAsync(command.UserId, cancellationToken);
        if (status is null)
            return Result.Failure(AuthorizationErrors.UserNotFound);

        if (!status.IsEnabled)
            return Result.Failure(MfaErrors.NotEnabled);

        if (!await _passwords.CheckPasswordAsync(command.UserId, command.CurrentPassword, cancellationToken))
            return Result.Failure(AuthenticationErrors.CurrentPasswordInvalid);

        var disabled = false;
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            disabled = await _mfa.DisableAndResetAsync(command.UserId, ct);
            if (!disabled)
                throw new InvalidOperationException("Identity MFA disable failed.");

            var now = _clock.UtcNow;
            await _refreshTokens.RevokeAllForUserAsync(command.UserId, now, ct);
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Mfa.Disabled,
                    now,
                    actorUserId: command.UserId,
                    targetUserId: command.UserId),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return Result.Success();
    }
}
