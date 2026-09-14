using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authentication.ChangePassword;

public sealed class ChangePasswordUseCase
{
    private readonly IIdentityPasswordChange _passwords;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ChangePasswordUseCase(
        IIdentityPasswordChange passwords,
        IRefreshTokenRepository refreshTokens,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _passwords = passwords;
        _refreshTokens = refreshTokens;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<ChangePasswordResult>> ExecuteAsync(
        ChangePasswordRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.UserId == Guid.Empty)
            return Result.Failure<ChangePasswordResult>(AuthorizationErrors.UserNotFound);

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            return Result.Failure<ChangePasswordResult>(AuthenticationErrors.CurrentPasswordInvalid);

        if (string.IsNullOrWhiteSpace(request.NewPassword))
            return Result.Failure<ChangePasswordResult>(AuthenticationErrors.InvalidPassword);

        Result<ChangePasswordResult>? outcome = null;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var changed = await _passwords.ChangePasswordAsync(
                request.UserId,
                request.CurrentPassword,
                request.NewPassword,
                ct);

            if (!changed.Succeeded)
            {
                outcome = MapFailure(changed);
                return;
            }

            var preserveCurrent = request.CurrentFamilyId is Guid familyId
                && familyId != Guid.Empty
                && await _refreshTokens.HasActiveFamilyForUserAsync(
                    request.UserId,
                    familyId,
                    _clock.UtcNow,
                    ct);

            if (preserveCurrent)
            {
                await _refreshTokens.RevokeAllForUserExceptFamilyAsync(
                    request.UserId,
                    request.CurrentFamilyId!.Value,
                    _clock.UtcNow,
                    ct);
            }
            else
            {
                await _refreshTokens.RevokeAllForUserAsync(
                    request.UserId,
                    _clock.UtcNow,
                    ct);
            }

            var now = _clock.UtcNow;
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Credentials.PasswordChanged,
                    now,
                    actorUserId: request.UserId,
                    targetUserId: request.UserId),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);

            outcome = Result.Success(new ChangePasswordResult
            {
                ReauthenticationRequired = !preserveCurrent
            });
        }, cancellationToken);

        return outcome ?? Result.Failure<ChangePasswordResult>(
            Error.Failure("Authentication.ChangePasswordFailed", "Password change failed."));
    }

    private static Result<ChangePasswordResult> MapFailure(IdentityPasswordChangeResult changed)
    {
        if (changed.UserNotFound)
            return Result.Failure<ChangePasswordResult>(AuthorizationErrors.UserNotFound);

        if (changed.InvalidCurrentPassword)
            return Result.Failure<ChangePasswordResult>(AuthenticationErrors.CurrentPasswordInvalid);

        if (changed.InvalidNewPassword)
            return Result.Failure<ChangePasswordResult>(AuthenticationErrors.InvalidPassword);

        return Result.Failure<ChangePasswordResult>(
            Error.Failure("Authentication.ChangePasswordFailed", "Password change failed."));
    }
}
