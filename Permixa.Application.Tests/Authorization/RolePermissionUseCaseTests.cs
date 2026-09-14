using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.RolePermissions.Assign;
using Permixa.Application.Authorization.RolePermissions.Remove;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class RolePermissionUseCaseTests
{
    private readonly Mock<IIdentityRoleReader> _roleReader = new();
    private readonly Mock<IPermissionRepository> _permissionRepository = new();
    private readonly Mock<IRolePermissionRepository> _rolePermissionRepository = new();
    private readonly Mock<IAuthorizationStateRepository> _stateRepository = new();
    private readonly Mock<IAuthorizationHierarchyService> _hierarchy = new();
    private readonly Mock<IEffectivePermissionService> _effectivePermissions = new();
    private readonly Mock<IIamAuditSink> _audit = new();
    private readonly Mock<IUnitOfWork> _unitOfWork = new();
    private readonly Mock<IClock> _clock = new();

    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private readonly Permission _permission = Permission.Create("Orders.Read");

    public RolePermissionUseCaseTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));
        _effectivePermissions
            .Setup(e => e.HasPermissionAsync(_actorId, IamPermissions.RolePermissions.Manage, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));
        _roleReader.Setup(r => r.RoleExistsAsync(_roleId, It.IsAny<CancellationToken>())).ReturnsAsync(true);
        _permissionRepository.Setup(p => p.GetByIdAsync(_permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(_permission);
        _hierarchy.Setup(h => h.CanManageRoleAsync(_actorId, _roleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task AssignPermissionToRole_IncrementsGlobalRbacVersionOnce_AndSavesOnce()
    {
        var state = AuthorizationState.CreateInitial();
        _stateRepository.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _rolePermissionRepository
            .Setup(r => r.ExistsAsync(_roleId, _permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateAssignUseCase();
        var result = await sut.ExecuteAsync(new AssignPermissionToRoleCommand(_actorId, _roleId, _permission.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(AuthorizationState.InitialRbacVersion + 1, state.RbacVersion);
        _rolePermissionRepository.Verify(
            r => r.AddAsync(It.IsAny<RolePermission>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AssignPermissionToRole_Duplicate_DoesNotIncrementVersion()
    {
        var state = AuthorizationState.CreateInitial();
        _stateRepository.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _rolePermissionRepository
            .Setup(r => r.ExistsAsync(_roleId, _permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateAssignUseCase();
        var result = await sut.ExecuteAsync(new AssignPermissionToRoleCommand(_actorId, _roleId, _permission.Id));

        Assert.Equal(AuthorizationErrors.RolePermissionAlreadyExists, result.Error);
        Assert.Equal(AuthorizationState.InitialRbacVersion, state.RbacVersion);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RemovePermissionFromRole_IncrementsGlobalRbacVersionOnce()
    {
        var state = AuthorizationState.CreateInitial();
        state.IncrementRbacVersion();
        _stateRepository.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _rolePermissionRepository
            .Setup(r => r.ExistsAsync(_roleId, _permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var sut = CreateRemoveUseCase();
        var before = state.RbacVersion;
        var result = await sut.ExecuteAsync(new RemovePermissionFromRoleCommand(_actorId, _roleId, _permission.Id));

        Assert.True(result.IsSuccess);
        Assert.Equal(before + 1, state.RbacVersion);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RemovePermissionFromRole_MissingRelationship_DoesNotIncrementVersion()
    {
        var state = AuthorizationState.CreateInitial();
        _stateRepository.Setup(s => s.GetAsync(It.IsAny<CancellationToken>())).ReturnsAsync(state);
        _rolePermissionRepository
            .Setup(r => r.ExistsAsync(_roleId, _permission.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var sut = CreateRemoveUseCase();
        var result = await sut.ExecuteAsync(new RemovePermissionFromRoleCommand(_actorId, _roleId, _permission.Id));

        Assert.Equal(AuthorizationErrors.RolePermissionNotFound, result.Error);
        Assert.Equal(AuthorizationState.InitialRbacVersion, state.RbacVersion);
        _unitOfWork.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private AssignPermissionToRoleUseCase CreateAssignUseCase() =>
        new(
            _roleReader.Object,
            _permissionRepository.Object,
            _rolePermissionRepository.Object,
            _stateRepository.Object,
            _hierarchy.Object,
            _effectivePermissions.Object,
            _audit.Object,
            _unitOfWork.Object,
            _clock.Object);

    private RemovePermissionFromRoleUseCase CreateRemoveUseCase() =>
        new(
            _roleReader.Object,
            _permissionRepository.Object,
            _rolePermissionRepository.Object,
            _stateRepository.Object,
            _hierarchy.Object,
            _effectivePermissions.Object,
            _audit.Object,
            _unitOfWork.Object,
            _clock.Object);
}
