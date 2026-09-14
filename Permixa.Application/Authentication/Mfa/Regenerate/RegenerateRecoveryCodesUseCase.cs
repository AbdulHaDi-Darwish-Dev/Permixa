using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Microsoft.Extensions.Options;

namespace Permixa.Application.Authentication.Mfa.Regenerate;

public sealed class RegenerateRecoveryCodesUseCase
{
    private readonly IIdentityMfa _mfa;
    private readonly IIdentityPasswordChange _passwords;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly PermixaMfaOptions _options;

    public RegenerateRecoveryCodesUseCase(
        IIdentityMfa mfa,
        IIdentityPasswordChange passwords,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock,
        IOptions<PermixaMfaOptions> options)
    {
        _mfa = mfa;
        _passwords = passwords;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _options = options.Value;
    }

    public async Task<Result<RegenerateRecoveryCodesResult>> ExecuteAsync(
        RegenerateRecoveryCodesCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty)
            return Result.Failure<RegenerateRecoveryCodesResult>(AuthorizationErrors.UserNotFound);

        if (string.IsNullOrWhiteSpace(command.CurrentPassword))
            return Result.Failure<RegenerateRecoveryCodesResult>(AuthenticationErrors.CurrentPasswordInvalid);

        var status = await _mfa.GetStatusAsync(command.UserId, cancellationToken);
        if (status is null)
            return Result.Failure<RegenerateRecoveryCodesResult>(AuthorizationErrors.UserNotFound);

        if (!status.IsEnabled)
            return Result.Failure<RegenerateRecoveryCodesResult>(MfaErrors.NotEnabled);

        if (!await _passwords.CheckPasswordAsync(command.UserId, command.CurrentPassword, cancellationToken))
            return Result.Failure<RegenerateRecoveryCodesResult>(AuthenticationErrors.CurrentPasswordInvalid);

        IReadOnlyList<string>? codes = null;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var generated = await _mfa.RegenerateRecoveryCodesAsync(
                command.UserId,
                _options.RecoveryCodeCount,
                ct);

            if (generated.NotEnabled || !generated.Succeeded)
                return;

            var now = _clock.UtcNow;
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Mfa.RecoveryCodesRegenerated,
                    now,
                    actorUserId: command.UserId,
                    targetUserId: command.UserId),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);
            codes = generated.RecoveryCodes;
        }, cancellationToken);

        if (codes is null)
            return Result.Failure<RegenerateRecoveryCodesResult>(MfaErrors.NotEnabled);

        return Result.Success(new RegenerateRecoveryCodesResult(codes));
    }
}
