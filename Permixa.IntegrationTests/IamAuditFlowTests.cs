using Permixa.Application.Audit;
using Permixa.Application.Audit.Get;
using Permixa.Application.Authentication.ChangePassword;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Roles.Create;
using Permixa.Application.Authorization.UserRoles.Assign;
using Permixa.Application.Authorization.Users.Create;
using Permixa.Application.Authorization.Users.Disable;
using Permixa.Application.Authorization.Users.Lock;
using Permixa.Application.Common.Paging;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Persistence;
using Permixa.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.IntegrationTests;

public sealed class IamAuditFlowTests : IntegrationTestBase
{
    public IamAuditFlowTests(SqlServerContainerFixture sql, RedisContainerFixture redis)
        : base(sql, redis)
    {
    }

    [SkippableFact]
    public async Task OwnerAdministrationFlow_ProducesNewestFirstAuditTrail()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.BootstrapAsync();

        var owner = await host.LoginAsync(TestKeys.OwnerEmail, TestKeys.OwnerPassword);
        var ownerRoleId = await host.ExecuteScopedAsync(async sp =>
            (await sp.GetRequiredService<ApplicationDbContext>().Roles
                .SingleAsync(r => r.Name == SystemRoles.Owner)).Id);

        var adminRole = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<CreateRoleUseCase>().ExecuteAsync(
                new CreateRoleCommand(owner.UserId, "Audit.Admin", ownerRoleId, RolePlacement.Below)));
        Assert.True(adminRole.IsSuccess, adminRole.Error?.Description);

        var adminUser = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<AdminCreateUserUseCase>().ExecuteAsync(
                new AdminCreateUserCommand(
                    owner.UserId,
                    "audit.admin@permixa.test",
                    "audit.admin",
                    TestKeys.UserPassword)));
        Assert.True(adminUser.IsSuccess, adminUser.Error?.Description);

        var assignedRole = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<AssignRoleToUserUseCase>().ExecuteAsync(
                new AssignRoleToUserCommand(owner.UserId, adminUser.Value.Id, adminRole.Value.Id)));
        Assert.True(assignedRole.IsSuccess, assignedRole.Error?.Description);

        await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            foreach (var name in new[]
                     {
                         IamPermissions.Users.Read,
                         IamPermissions.Users.Create,
                         IamPermissions.Users.Lock,
                         IamPermissions.Users.Disable
                     })
            {
                var permissionId = (await db.Permissions.SingleAsync(p => p.Name == name)).Id;
                await host.AssignPermissionToRoleAsync(owner.UserId, adminRole.Value.Id, permissionId);
            }
        });

        var subject = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<AdminCreateUserUseCase>().ExecuteAsync(
                new AdminCreateUserCommand(
                    adminUser.Value.Id,
                    "audit.subject@permixa.test",
                    "audit.subject",
                    TestKeys.UserPassword)));
        Assert.True(subject.IsSuccess, subject.Error?.Description);

        var lockedUntil = DateTime.UtcNow.AddHours(2);
        var locked = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<LockUserUseCase>().ExecuteAsync(
                new LockUserCommand(adminUser.Value.Id, subject.Value.Id, lockedUntil)));
        Assert.True(locked.IsSuccess, locked.Error?.Description);

        var unlocked = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<UnlockUserUseCase>().ExecuteAsync(
                new UnlockUserCommand(adminUser.Value.Id, subject.Value.Id)));
        Assert.True(unlocked.IsSuccess, unlocked.Error?.Description);

        var disabled = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<DisableUserUseCase>().ExecuteAsync(
                new DisableUserCommand(adminUser.Value.Id, subject.Value.Id)));
        Assert.True(disabled.IsSuccess, disabled.Error?.Description);

        var enabled = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<EnableUserUseCase>().ExecuteAsync(
                new EnableUserCommand(adminUser.Value.Id, subject.Value.Id)));
        Assert.True(enabled.IsSuccess, enabled.Error?.Description);

        var subjectSession = await host.LoginAsync("audit.subject@permixa.test", TestKeys.UserPassword);
        var passwordChanged = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<ChangePasswordUseCase>().ExecuteAsync(new ChangePasswordRequest
            {
                UserId = subject.Value.Id,
                CurrentPassword = TestKeys.UserPassword,
                NewPassword = "AuditSubj3ct!",
                CurrentFamilyId = null
            }));
        Assert.True(passwordChanged.IsSuccess, passwordChanged.Error?.Description);

        var auditPage = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<GetIamAuditLogsUseCase>().ExecuteAsync(
                new GetIamAuditLogsQuery(owner.UserId, new PageRequest(1, 100))));

        Assert.True(auditPage.IsSuccess, auditPage.Error?.Description);
        var eventTypes = auditPage.Value.Items.Select(i => i.EventType).ToList();

        Assert.Contains(IamAuditEvents.Roles.Created, eventTypes);
        Assert.Contains(IamAuditEvents.UserRoles.Assigned, eventTypes);
        Assert.Contains(IamAuditEvents.Users.Created, eventTypes);
        Assert.Contains(IamAuditEvents.Users.Locked, eventTypes);
        Assert.Contains(IamAuditEvents.Users.Unlocked, eventTypes);
        Assert.Contains(IamAuditEvents.Users.Disabled, eventTypes);
        Assert.Contains(IamAuditEvents.Users.Enabled, eventTypes);
        Assert.Contains(IamAuditEvents.Credentials.PasswordChanged, eventTypes);

        Assert.Contains(
            auditPage.Value.Items,
            i => i.EventType == IamAuditEvents.Credentials.PasswordChanged
                 && i.ActorUserId == subject.Value.Id
                 && i.TargetUserId == subject.Value.Id);

        var subjectLifecycle = new[]
        {
            IamAuditEvents.Users.Created,
            IamAuditEvents.Users.Locked,
            IamAuditEvents.Users.Unlocked,
            IamAuditEvents.Users.Disabled,
            IamAuditEvents.Users.Enabled
        };
        foreach (var expected in subjectLifecycle)
        {
            Assert.Contains(
                auditPage.Value.Items,
                i => i.EventType == expected && i.TargetUserId == subject.Value.Id);
        }

        // OccurredAtUtc DESC is required. Id DESC is SQL Server uniqueidentifier order
        // (not .NET Guid.CompareTo), so only assert time is non-increasing here.
        for (var i = 1; i < auditPage.Value.Items.Count; i++)
        {
            Assert.True(
                auditPage.Value.Items[i - 1].OccurredAtUtc >= auditPage.Value.Items[i].OccurredAtUtc);
        }

        _ = subjectSession;
    }
}
