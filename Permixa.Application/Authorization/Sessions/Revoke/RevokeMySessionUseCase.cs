using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Sessions.Revoke;

public sealed class RevokeMySessionUseCase
{
    private readonly IIdentityUserReader _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public RevokeMySessionUseCase(
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
        RevokeMySessionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty || !await _users.UserExistsAsync(command.UserId, cancellationToken))
            return Result.Failure(AuthorizationErrors.UserNotFound);

        if (!await _refreshTokens.FamilyExistsForUserAsync(command.UserId, command.FamilyId, cancellationToken))
            return Result.Failure(AuthorizationErrors.SessionNotFound);

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var now = _clock.UtcNow;
            await _refreshTokens.RevokeFamilyForUserAsync(command.UserId, command.FamilyId, now, ct);
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Sessions.Revoked,
                    now,
                    actorUserId: command.UserId,
                    targetUserId: command.UserId,
                    metadata: new Dictionary<string, string>
                    {
                        ["FamilyId"] = command.FamilyId.ToString()
                    }),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return Result.Success();
    }
}
