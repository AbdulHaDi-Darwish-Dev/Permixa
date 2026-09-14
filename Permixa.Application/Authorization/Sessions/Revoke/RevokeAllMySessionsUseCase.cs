using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Sessions.Revoke;

public sealed class RevokeAllMySessionsUseCase
{
    private readonly IIdentityUserReader _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RevokeAllMySessionsUseCase(
        IIdentityUserReader users,
        IRefreshTokenRepository refreshTokens,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        RevokeAllMySessionsCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty || !await _users.UserExistsAsync(command.UserId, cancellationToken))
            return Result.Failure(AuthorizationErrors.UserNotFound);

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var now = _clock.UtcNow;
            await _refreshTokens.RevokeAllForUserAsync(command.UserId, now, ct);
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Sessions.RevokedAll,
                    now,
                    actorUserId: command.UserId,
                    targetUserId: command.UserId),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return Result.Success();
    }
}
