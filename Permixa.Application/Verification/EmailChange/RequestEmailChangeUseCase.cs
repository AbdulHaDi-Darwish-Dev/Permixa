using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Verification.Abstractions;

namespace Permixa.Application.Verification.EmailChange;

public sealed class RequestEmailChangeUseCase
{
    private readonly IIdentityPasswordChange _passwords;
    private readonly IIdentityUserEmailReader _emails;
    private readonly PendingEmailChangeService _pending;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RequestEmailChangeUseCase(
        IIdentityPasswordChange passwords,
        IIdentityUserEmailReader emails,
        PendingEmailChangeService pending,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _passwords = passwords;
        _emails = emails;
        _pending = pending;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        RequestEmailChangeRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.UserId == Guid.Empty)
            return Result.Failure(VerificationErrors.UserNotFound);

        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
            return Result.Failure(AuthenticationErrors.CurrentPasswordInvalid);

        if (string.IsNullOrWhiteSpace(request.NewEmail))
            return Result.Failure(AuthenticationErrors.InvalidEmail);

        var profile = await _emails.GetEmailProfileAsync(request.UserId, cancellationToken);
        if (profile is null)
            return Result.Failure(VerificationErrors.UserNotFound);

        if (!await _passwords.CheckPasswordAsync(request.UserId, request.CurrentPassword, cancellationToken))
            return Result.Failure(AuthenticationErrors.CurrentPasswordInvalid);

        var result = await _pending.RequestAsync(request.UserId, request.NewEmail, cancellationToken);
        if (result.IsSuccess)
        {
            var now = _clock.UtcNow;
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Credentials.EmailChangeRequested,
                    now,
                    actorUserId: request.UserId,
                    targetUserId: request.UserId),
                cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return result;
    }
}
