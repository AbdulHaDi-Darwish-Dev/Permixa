using Permixa.Domain.Audit;
using Permixa.Domain.Common;

namespace Permixa.Domain.Tests.Audit;

public sealed class IamAuditLogTests
{
    private static readonly DateTime Now = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_PersistsIdentifiersWithoutRequiringTargets()
    {
        var log = IamAuditLog.Create(
            "User.Disabled",
            "Success",
            Now,
            actorUserId: Guid.NewGuid(),
            targetUserId: Guid.NewGuid());

        Assert.Equal("User.Disabled", log.EventType);
        Assert.Equal("Success", log.Outcome);
        Assert.Null(log.MetadataJson);
        Assert.Null(log.TargetRoleId);
    }

    [Fact]
    public void Create_RejectsOversizedMetadata()
    {
        var huge = new string('x', IamAuditLog.MaxMetadataJsonLength + 1);
        Assert.Throws<DomainException>(() =>
            IamAuditLog.Create("Role.Created", "Success", Now, metadataJson: huge));
    }

    [Fact]
    public void Create_RejectsEmptyEventType()
    {
        Assert.Throws<DomainException>(() =>
            IamAuditLog.Create(" ", "Success", Now));
    }
}
