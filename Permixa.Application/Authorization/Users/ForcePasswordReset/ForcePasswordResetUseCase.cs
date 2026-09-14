using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Application.Verification;
using Permixa.Application.Verification.Abstractions;
using Permixa.Domain.Verification;

namespace Permixa.Application.Authorization.Users.ForcePasswordReset;

public sealed class ForcePasswordResetUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IAuthorizationHierarchyService _hierarchy;
    private readonly IIdentityUserReader _users;
    private readonly IIdentityUserEmailReader _emails;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly VerificationChallengeIssuer _issuer;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public ForcePasswordResetUseCase(
        IEffectivePermissionService effectivePermissions,
        IAuthorizationHierarchyService hierarchy,
        IIdentityUserReader users,
        IIdentityUserEmailReader emails,
        IRefreshTokenRepository refreshTokens,
        VerificationChallengeIssuer issuer,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _hierarchy = hierarchy;
        _users = users;
        _emails = emails;
        _refreshTokens = refreshTokens;
        _issuer = issuer;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> ExecuteAsync(
        ForcePasswordResetCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var self = UserAdministrationGuard.RejectSelf(command.ActorUserId, command.TargetUserId);
        if (self is not null)
            return self;

        var permission = await UserAdministrationGuard.EnsurePermissionAsync(
            _effectivePermissions,
            command.ActorUserId,
            IamPermissions.Users.ForcePasswordReset,
            cancellationToken);
        if (permission is not null)
            return permission;

        if (!await _users.UserExistsAsync(command.TargetUserId, cancellationToken))
            return Result.Failure(AuthorizationErrors.UserNotFound);

        var owner = await UserAdministrationGuard.RejectOwnerAsync(
            _users, command.TargetUserId, cancellationToken);
        if (owner is not null)
            return owner;

        var hierarchy = await UserAdministrationGuard.EnsureCanManageAsync(
            _hierarchy, command.ActorUserId, command.TargetUserId, cancellationToken);
        if (hierarchy is not null)
            return hierarchy;

        var email = await _emails.GetEmailAsync(command.TargetUserId, cancellationToken);
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure(AuthorizationErrors.InvalidEmail);

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var now = _clock.UtcNow;
            await _refreshTokens.RevokeAllForUserAsync(command.TargetUserId, now, ct);
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Credentials.PasswordResetForced,
                    now,
                    actorUserId: command.ActorUserId,
                    targetUserId: command.TargetUserId),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);

        return await _issuer.IssueAsync(
            command.TargetUserId,
            VerificationPurpose.PasswordReset,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            email,
            cancellationToken);
    }
}
