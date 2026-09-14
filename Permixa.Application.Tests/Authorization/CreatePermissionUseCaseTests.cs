using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Permissions.Create;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Domain.Authorization;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class CreatePermissionUseCaseTests
{
    private readonly Mock<IEffectivePermissionService> _effectivePermissions = new();
    private readonly Mock<IPermissionRepository> _repo = new();
    private readonly Mock<IIamAuditSink> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Guid _actorId = Guid.NewGuid();

    public CreatePermissionUseCaseTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTime(2026, 5, 1, 12, 0, 0, DateTimeKind.Utc));
        GrantCreate(true);
    }

    [Fact]
    public async Task CreatePermission_PersistsUniqueValidName()
    {
        _repo.Setup(r => r.ExistsByNameAsync("Users.Read", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        Permission? added = null;
        _repo.Setup(r => r.AddAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()))
            .Callback<Permission, CancellationToken>((p, _) => added = p)
            .Returns(Task.CompletedTask);

        var result = await CreateSut().ExecuteAsync(new CreatePermissionCommand(_actorId, " Users.Read ", "Read users"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Users.Read", result.Value.Name);
        Assert.NotNull(added);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CreatePermission_DuplicateName_DoesNotSave()
    {
        _repo.Setup(r => r.ExistsByNameAsync("Users.Read", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var result = await CreateSut().ExecuteAsync(new CreatePermissionCommand(_actorId, "Users.Read", null));

        Assert.True(result.IsFailure);
        Assert.Equal(AuthorizationErrors.PermissionAlreadyExists, result.Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreatePermission_MissingIamCreate_IsForbidden()
    {
        GrantCreate(false);

        var result = await CreateSut().ExecuteAsync(new CreatePermissionCommand(_actorId, "Users.Read", null));

        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _repo.Verify(r => r.AddAsync(It.IsAny<Permission>(), It.IsAny<CancellationToken>()), Times.Never);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private void GrantCreate(bool allowed) =>
        _effectivePermissions
            .Setup(e => e.HasPermissionAsync(_actorId, IamPermissions.Permissions.Create, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(allowed));

    private CreatePermissionUseCase CreateSut() =>
        new(_effectivePermissions.Object, _repo.Object, _audit.Object, _uow.Object, _clock.Object);
}
