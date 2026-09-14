using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Sessions;
using Permixa.Application.Authorization.Sessions.Get;
using Permixa.Application.Authorization.Sessions.Models;
using Permixa.Application.Authorization.Sessions.Revoke;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class SessionAdministrationUseCaseTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 18, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IEffectivePermissionService> _permissions = new();
    private readonly Mock<IAuthorizationHierarchyService> _hierarchy = new();
    private readonly Mock<IIdentityUserReader> _users = new();
    private readonly Mock<ISessionReader> _sessions = new();
    private readonly Mock<IRefreshTokenRepository> _refresh = new();
    private readonly Mock<IIamAuditSink> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClock> _clock = new();

    private readonly Guid _actor = Guid.NewGuid();
    private readonly Guid _target = Guid.NewGuid();
    private readonly Guid _owner = Guid.NewGuid();
    private readonly Guid _family = Guid.NewGuid();

    public SessionAdministrationUseCaseTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((action, ct) => action(ct));
        Grant(IamPermissions.Sessions.Read, true);
        Grant(IamPermissions.Sessions.Revoke, true);
        _hierarchy.Setup(h => h.CanManageUserAsync(_actor, _target, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.UserExistsAsync(_actor, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.UserExistsAsync(_target, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.UserExistsAsync(_owner, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _users.Setup(u => u.HasRoleNameAsync(_target, PermixaRoles.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        _users.Setup(u => u.HasRoleNameAsync(_owner, PermixaRoles.Owner, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    }

    [Fact]
    public async Task GetMySessions_NoIamPermission_ReturnsCallerFamiliesOnly()
    {
        var other = Guid.NewGuid();
        _sessions.Setup(s => s.GetActiveFamiliesForUserAsync(_actor, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync([new SessionFamilyRecord(_family, Now.AddHours(-2), Now.AddDays(1))]);

        var result = await new GetMySessionsUseCase(_users.Object, _sessions.Object, _clock.Object)
            .ExecuteAsync(new GetMySessionsQuery(_actor, _family));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value);
        Assert.True(result.Value[0].IsCurrent);
        _permissions.Verify(
            p => p.HasPermissionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _sessions.Verify(
            s => s.GetActiveFamiliesForUserAsync(other, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetUserSessions_MissingPermission_IsForbidden()
    {
        Grant(IamPermissions.Sessions.Read, false);
        var result = await GetUserSessionsSut().ExecuteAsync(new GetUserSessionsQuery(_actor, _target));
        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _sessions.Verify(
            s => s.GetActiveFamiliesForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task GetUserSessions_Self_IsDenied()
    {
        var result = await GetUserSessionsSut().ExecuteAsync(new GetUserSessionsQuery(_actor, _actor));
        Assert.Equal(AuthorizationErrors.CannotManageSelf, result.Error);
    }

    [Fact]
    public async Task GetUserSessions_Owner_IsProtected()
    {
        var result = await GetUserSessionsSut().ExecuteAsync(new GetUserSessionsQuery(_actor, _owner));
        Assert.Equal(AuthorizationErrors.OwnerProtected, result.Error);
    }

    [Fact]
    public async Task RevokeMySession_OtherUsersFamily_IsNotFound()
    {
        _refresh.Setup(r => r.FamilyExistsForUserAsync(_actor, _family, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await new RevokeMySessionUseCase(_users.Object, _refresh.Object, _audit.Object, _uow.Object, _clock.Object)
            .ExecuteAsync(new RevokeMySessionCommand(_actor, _family));

        Assert.Equal(AuthorizationErrors.SessionNotFound, result.Error);
        _refresh.Verify(
            r => r.RevokeFamilyForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RevokeUserSession_WrongTargetFamily_DoesNotRevoke()
    {
        _refresh.Setup(r => r.FamilyExistsForUserAsync(_target, _family, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await RevokeUserSessionSut().ExecuteAsync(
            new RevokeUserSessionCommand(_actor, _target, _family));

        Assert.Equal(AuthorizationErrors.SessionNotFound, result.Error);
        _refresh.Verify(
            r => r.RevokeFamilyForUserAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RevokeAllUserSessions_Peer_IsDenied()
    {
        _hierarchy.Setup(h => h.CanManageUserAsync(_actor, _target, It.IsAny<CancellationToken>())).ReturnsAsync(false);
        var result = await RevokeAllUserSut().ExecuteAsync(new RevokeAllUserSessionsCommand(_actor, _target));
        Assert.Equal(AuthorizationErrors.HierarchyViolation, result.Error);
        _refresh.Verify(
            r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RevokeAllMySessions_CallsBulkPrimitive()
    {
        var result = await new RevokeAllMySessionsUseCase(_users.Object, _refresh.Object, _audit.Object, _uow.Object, _clock.Object)
            .ExecuteAsync(new RevokeAllMySessionsCommand(_actor));

        Assert.True(result.IsSuccess);
        _refresh.Verify(r => r.RevokeAllForUserAsync(_actor, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    private void Grant(string permission, bool allowed) =>
        _permissions.Setup(p => p.HasPermissionAsync(_actor, permission, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(allowed));

    private GetUserSessionsUseCase GetUserSessionsSut() =>
        new(_permissions.Object, _hierarchy.Object, _users.Object, _sessions.Object, _clock.Object);

    private RevokeUserSessionUseCase RevokeUserSessionSut() =>
        new(_permissions.Object, _hierarchy.Object, _users.Object, _refresh.Object, _audit.Object, _uow.Object, _clock.Object);

    private RevokeAllUserSessionsUseCase RevokeAllUserSut() =>
        new(_permissions.Object, _hierarchy.Object, _users.Object, _refresh.Object, _audit.Object, _uow.Object, _clock.Object);
}
