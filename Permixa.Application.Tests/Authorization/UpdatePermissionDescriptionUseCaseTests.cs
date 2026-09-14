using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Permissions.Update;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Domain.Authorization;
using Moq;

namespace Permixa.Application.Tests.Authorization;

public sealed class UpdatePermissionDescriptionUseCaseTests
{
    private readonly Mock<IEffectivePermissionService> _effectivePermissions = new();
    private readonly Mock<IPermissionRepository> _repo = new();
    private readonly Mock<IIamAuditSink> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Guid _actorId = Guid.NewGuid();
    private readonly Permission _permission = Permission.Create("Orders.Read", "Original");

    public UpdatePermissionDescriptionUseCaseTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(new DateTime(2026, 8, 1, 9, 0, 0, DateTimeKind.Utc));
        GrantUpdate(true);
        _repo.Setup(r => r.GetByIdAsync(_permission.Id, It.IsAny<CancellationToken>())).ReturnsAsync(_permission);
    }

    [Fact]
    public async Task UpdateDescription_Succeeds_WithoutChangingName()
    {
        var result = await CreateSut().ExecuteAsync(
            new UpdatePermissionDescriptionCommand(_actorId, _permission.Id, "Updated"));

        Assert.True(result.IsSuccess);
        Assert.Equal("Orders.Read", result.Value.Name);
        Assert.Equal("Updated", result.Value.Description);
        Assert.Equal("Orders.Read", _permission.Name);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateDescription_MissingUpdate_IsForbidden()
    {
        GrantUpdate(false);

        var result = await CreateSut().ExecuteAsync(
            new UpdatePermissionDescriptionCommand(_actorId, _permission.Id, "Updated"));

        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        Assert.Equal("Original", _permission.Description);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateDescription_MissingPermission_IsNotFound()
    {
        var missingId = Guid.NewGuid();
        _repo.Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>())).ReturnsAsync((Permission?)null);

        var result = await CreateSut().ExecuteAsync(
            new UpdatePermissionDescriptionCommand(_actorId, missingId, "Updated"));

        Assert.Equal(AuthorizationErrors.PermissionNotFound, result.Error);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateDescription_SameNormalizedValue_IsIdempotent()
    {
        var result = await CreateSut().ExecuteAsync(
            new UpdatePermissionDescriptionCommand(_actorId, _permission.Id, " Original "));

        Assert.True(result.IsSuccess);
        Assert.Equal("Original", result.Value.Description);
        _uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private void GrantUpdate(bool allowed) =>
        _effectivePermissions
            .Setup(e => e.HasPermissionAsync(_actorId, IamPermissions.Permissions.Update, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(allowed));

    private UpdatePermissionDescriptionUseCase CreateSut() =>
        new(_effectivePermissions.Object, _repo.Object, _audit.Object, _uow.Object, _clock.Object);
}
