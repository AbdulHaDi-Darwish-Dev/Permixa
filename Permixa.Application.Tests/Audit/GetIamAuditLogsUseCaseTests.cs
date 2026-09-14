using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Audit.Get;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Paging;
using Permixa.Application.Common.Results;
using Moq;

namespace Permixa.Application.Tests.Audit;

public sealed class GetIamAuditLogsUseCaseTests
{
    private readonly Mock<IEffectivePermissionService> _permissions = new();
    private readonly Mock<IIamAuditReader> _reader = new();
    private readonly Guid _actor = Guid.NewGuid();

    [Fact]
    public async Task MissingPermission_ReturnsForbidden()
    {
        _permissions.Setup(p => p.HasPermissionAsync(_actor, IamPermissions.Audit.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(false));

        var result = await Sut().ExecuteAsync(new GetIamAuditLogsQuery(_actor, new PageRequest()));

        Assert.Equal(AuthorizationErrors.MissingManagePermission, result.Error);
        _reader.Verify(r => r.SearchAsync(It.IsAny<IamAuditSearchQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WithPermission_ReturnsPagedRows_WithoutHierarchyFilter()
    {
        _permissions.Setup(p => p.HasPermissionAsync(_actor, IamPermissions.Audit.Read, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(true));

        var row = new IamAuditLogRecord(
            Guid.NewGuid(),
            DateTime.UtcNow,
            IamAuditEvents.Users.Locked,
            IamAuditOutcome.Success,
            _actor,
            Guid.NewGuid(),
            null,
            null,
            null,
            null);

        _reader.Setup(r => r.SearchAsync(It.IsAny<IamAuditSearchQuery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PagedResult<IamAuditLogRecord>([row], 1, 20, 1));

        var result = await Sut().ExecuteAsync(new GetIamAuditLogsQuery(
            _actor,
            new PageRequest(1, 20),
            FilterActorUserId: _actor,
            EventType: IamAuditEvents.Users.Locked,
            Outcome: IamAuditOutcome.Success));

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal(IamAuditEvents.Users.Locked, result.Value.Items[0].EventType);
    }

    [Fact]
    public async Task InvalidPaging_Fails()
    {
        var result = await Sut().ExecuteAsync(new GetIamAuditLogsQuery(_actor, new PageRequest(0, 20)));
        Assert.Equal(AuthorizationErrors.InvalidPaging, result.Error);
    }

    private GetIamAuditLogsUseCase Sut() => new(_permissions.Object, _reader.Object);
}
