using System.Net.Http.Json;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Roles.Create;
using Permixa.Application.Authorization.UserRoles.Assign;
using Permixa.Application.Authorization.Users.Create;
using Permixa.Application.Authorization.Users.Disable;
using Permixa.Application.Authorization.Users.Get;
using Permixa.Application.Authorization.Users.Lock;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Persistence;
using Permixa.IntegrationTests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.IntegrationTests;

public sealed class UserAdministrationFlowTests : IntegrationTestBase
{
    public UserAdministrationFlowTests(SqlServerContainerFixture sql, RedisContainerFixture redis)
        : base(sql, redis)
    {
    }

    [SkippableFact]
    public async Task OwnerAndAdmin_AdministerLowerUser_LockUnlockDisableEnable()
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
                new CreateRoleCommand(owner.UserId, "Admin", ownerRoleId, RolePlacement.Below)));
        Assert.True(adminRole.IsSuccess, adminRole.Error?.Description);

        var adminUser = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<AdminCreateUserUseCase>().ExecuteAsync(
                new AdminCreateUserCommand(
                    owner.UserId, "admin.flow@permixa.test", "admin.flow", TestKeys.UserPassword)));
        Assert.True(adminUser.IsSuccess, adminUser.Error?.Description);
        Assert.False(adminUser.Value.EmailConfirmed);
        Assert.Null(adminUser.Value.EffectiveRoleLevel);

        var assigned = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<AssignRoleToUserUseCase>().ExecuteAsync(
                new AssignRoleToUserCommand(owner.UserId, adminUser.Value.Id, adminRole.Value.Id)));
        Assert.True(assigned.IsSuccess, assigned.Error?.Description);

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

        var lower = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<AdminCreateUserUseCase>().ExecuteAsync(
                new AdminCreateUserCommand(
                    adminUser.Value.Id, "lower.flow@permixa.test", "lower.flow", TestKeys.UserPassword)));
        Assert.True(lower.IsSuccess, lower.Error?.Description);

        var read = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<GetUserByIdUseCase>().ExecuteAsync(
                new GetUserByIdQuery(adminUser.Value.Id, lower.Value.Id)));
        Assert.True(read.IsSuccess, read.Error?.Description);
        Assert.Equal("lower.flow", read.Value.UserName);

        var lockedUntil = DateTime.UtcNow.AddHours(2);
        var locked = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<LockUserUseCase>().ExecuteAsync(
                new LockUserCommand(adminUser.Value.Id, lower.Value.Id, lockedUntil)));
        Assert.True(locked.IsSuccess, locked.Error?.Description);

        await ProblemJson.ReadAndAssertAsync(
            await host.LoginRawAsync("lower.flow@permixa.test", TestKeys.UserPassword),
            401,
            AuthenticationErrors.LockedOut.Code);

        var unlocked = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<UnlockUserUseCase>().ExecuteAsync(
                new UnlockUserCommand(adminUser.Value.Id, lower.Value.Id)));
        Assert.True(unlocked.IsSuccess, unlocked.Error?.Description);

        var session = await host.LoginAsync("lower.flow@permixa.test", TestKeys.UserPassword);
        Assert.False(string.IsNullOrWhiteSpace(session.RefreshToken));

        var disabled = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<DisableUserUseCase>().ExecuteAsync(
                new DisableUserCommand(adminUser.Value.Id, lower.Value.Id)));
        Assert.True(disabled.IsSuccess, disabled.Error?.Description);

        await ProblemJson.ReadAndAssertAsync(
            await host.LoginRawAsync("lower.flow@permixa.test", TestKeys.UserPassword),
            401,
            AuthenticationErrors.InvalidCredentials.Code);

        await ProblemJson.ReadAndAssertAsync(
            await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest { RefreshToken = session.RefreshToken }),
            401,
            AuthenticationErrors.RefreshTokenRevoked.Code);

        var enabled = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<EnableUserUseCase>().ExecuteAsync(
                new EnableUserCommand(adminUser.Value.Id, lower.Value.Id)));
        Assert.True(enabled.IsSuccess, enabled.Error?.Description);

        await ProblemJson.ReadAndAssertAsync(
            await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest { RefreshToken = session.RefreshToken }),
            401,
            AuthenticationErrors.RefreshTokenRevoked.Code);

        var again = await host.LoginAsync("lower.flow@permixa.test", TestKeys.UserPassword);
        Assert.NotEqual(session.RefreshToken, again.RefreshToken);
        Assert.IsType<AuthenticationResult>(again);
    }
}
