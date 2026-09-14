using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Users.ChangeEmail;
using Permixa.Application.Authorization.Users.ForcePasswordReset;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Application.Verification;
using Permixa.Application.Verification.Abstractions;
using Permixa.Application.Verification.EmailChange;
using Permixa.Domain.Authorization;
using Permixa.Domain.Verification;
using Microsoft.Extensions.Options;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class CredentialAdministrationUseCaseTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 20, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IEffectivePermissionService> _permissions = new();
    private readonly Mock<IAuthorizationHierarchyService> _hierarchy = new();
    private readonly Mock<IIdentityUserReader> _users = new();
    private readonly Mock<IIdentityUserEmailReader> _emails = new();
    private readonly Mock<IRefreshTokenRepository> _refresh = new();
    private readonly Mock<IIamAuditSink> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Guid _actor = Guid.NewGuid();
    private readonly Guid _target = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();

    public CredentialAdministrationUseCaseTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        Grant(IamPermissions.Users.ChangeEmail, true);
        Grant(IamPermissions.Users.ForcePasswordReset, true);
        _hierarchy.Setup(h => h.CanManageUserAsync(_actor, _target, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.UserExistsAsync(_target, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.UserExistsAsync(_owner, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.HasRoleNameAsync(_target, PermixaRoles.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _users.Setup(u => u.HasRoleNameAsync(_owner, PermixaRoles.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task AdminRequestEmailChange_MissingPermission_IsForbidden()
    {
        Grant(IamPermissions.Users.ChangeEmail, false);
        var result = await AdminEmailSut().ExecuteAsync(
            new AdminRequestEmailChangeCommand(_actor, _target, "new@example.com"));
        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
    }

    [Fact]
    public async Task AdminRequestEmailChange_Self_IsDenied()
    {
        var result = await AdminEmailSut().ExecuteAsync(
            new AdminRequestEmailChangeCommand(_actor, _actor, "new@example.com"));
        Assert.Equal(AuthorizationErrors.CannotManageSelf, result.Error);
    }

    [Fact]
    public async Task AdminRequestEmailChange_Owner_IsProtected()
    {
        var result = await AdminEmailSut().ExecuteAsync(
            new AdminRequestEmailChangeCommand(_actor, _owner, "new@example.com"));
        Assert.Equal(AuthorizationErrors.OwnerProtected, result.Error);
    }

    [Fact]
    public async Task AdminRequestEmailChange_Peer_IsDenied()
    {
        _hierarchy.Setup(h => h.CanManageUserAsync(_actor, _target, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var result = await AdminEmailSut().ExecuteAsync(
            new AdminRequestEmailChangeCommand(_actor, _target, "new@example.com"));
        Assert.Equal(AuthorizationErrors.HierarchyViolation, result.Error);
    }

    [Fact]
    public async Task ForcePasswordReset_MissingPermission_IsForbidden()
    {
        Grant(IamPermissions.Users.ForcePasswordReset, false);
        var result = await ForceResetSut().ExecuteAsync(new ForcePasswordResetCommand(_actor, _target));
        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _refresh.Verify(
            r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ForcePasswordReset_Self_IsDenied()
    {
        var result = await ForceResetSut().ExecuteAsync(new ForcePasswordResetCommand(_actor, _actor));
        Assert.Equal(AuthorizationErrors.CannotManageSelf, result.Error);
    }

    [Fact]
    public async Task ForcePasswordReset_Owner_IsProtected()
    {
        var result = await ForceResetSut().ExecuteAsync(new ForcePasswordResetCommand(_actor, _owner));
        Assert.Equal(AuthorizationErrors.OwnerProtected, result.Error);
    }

    [Fact]
    public async Task ForcePasswordReset_Peer_IsDenied()
    {
        _hierarchy.Setup(h => h.CanManageUserAsync(_actor, _target, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var result = await ForceResetSut().ExecuteAsync(new ForcePasswordResetCommand(_actor, _target));
        Assert.Equal(AuthorizationErrors.HierarchyViolation, result.Error);
        _refresh.Verify(
            r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private void Grant(string permission, bool allowed) =>
        _permissions.Setup(p => p.HasPermissionAsync(_actor, permission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(allowed));

    private AdminRequestEmailChangeUseCase AdminEmailSut()
    {
        var pending = new PendingEmailChangeService(
            _emails.Object,
            Mock.Of<IIdentityUserWriter>(),
            Mock.Of<IVerificationChallengeRepository>(),
            new VerificationChallengeIssuer(
                Mock.Of<IVerificationChallengeRepository>(),
                Mock.Of<IVerificationTokenProvider>(),
                Mock.Of<IVerificationDispatcher>(),
                Mock.Of<IUnitOfWork>(),
                _clock.Object,
                Mock.Of<IPersistenceExceptionClassifier>(),
                Options.Create(new PermixaVerificationOptions())),
            Mock.Of<IUnitOfWork>(),
            _clock.Object);

        return new AdminRequestEmailChangeUseCase(
            _permissions.Object,
            _hierarchy.Object,
            _users.Object,
            pending,
            _audit.Object,
            _uow.Object,
            _clock.Object);
    }

    private ForcePasswordResetUseCase ForceResetSut() =>
        new(
            _permissions.Object,
            _hierarchy.Object,
            _users.Object,
            _emails.Object,
            _refresh.Object,
            new VerificationChallengeIssuer(
                Mock.Of<IVerificationChallengeRepository>(),
                Mock.Of<IVerificationTokenProvider>(),
                Mock.Of<IVerificationDispatcher>(),
                Mock.Of<IUnitOfWork>(),
                _clock.Object,
                Mock.Of<IPersistenceExceptionClassifier>(),
                Options.Create(new PermixaVerificationOptions())),
            _audit.Object,
            _uow.Object,
            _clock.Object);
}
