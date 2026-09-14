using Permixa.Application.Authentication;
using Permixa.Application.Authorization;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Permixa.IntegrationTests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.IntegrationTests;

public sealed class BootstrapRegistrationTests : IntegrationTestBase
{
    public BootstrapRegistrationTests(SqlServerContainerFixture sql, RedisContainerFixture redis)
        : base(sql, redis)
    {
    }

    [SkippableFact]
    public async Task Bootstrap_CreatesOwnerState_AndIsIdempotent()
    {
        RequireContainers();
        await using var host = await StartHostAsync();

        await host.BootstrapAsync();

        await host.ExecuteScopedAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = sp.GetRequiredService<RoleManager<ApplicationRole>>();
            var db = sp.GetRequiredService<ApplicationDbContext>();

            var owner = await users.FindByEmailAsync(TestKeys.OwnerEmail);
            Assert.NotNull(owner);
            Assert.True(await users.IsInRoleAsync(owner!, SystemRoles.Owner));

            var ownerRole = await roles.FindByNameAsync(SystemRoles.Owner);
            Assert.NotNull(ownerRole);
            Assert.Equal(SystemRoles.OwnerRoleLevel, ownerRole!.RoleLevel);

            var permissionNames = await db.Permissions.Select(p => p.Name).ToListAsync();
            foreach (var name in IamPermissions.All)
                Assert.Contains(name, permissionNames);

            var ownerPermissionCount = await db.RolePermissions.CountAsync(rp => rp.RoleId == ownerRole.Id);
            Assert.Equal(IamPermissions.All.Count, ownerPermissionCount);

            var state = await db.AuthorizationStates.SingleAsync();
            Assert.Equal(AuthorizationState.GlobalId, state.Id);
            Assert.Equal(2, state.RbacVersion);
        });

        await host.BootstrapAsync();

        await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var roles = sp.GetRequiredService<RoleManager<ApplicationRole>>();

            Assert.Equal(1, await users.Users.CountAsync());
            Assert.Equal(1, await roles.Roles.CountAsync());
            Assert.Equal(IamPermissions.All.Count, await db.Permissions.CountAsync());
            Assert.Equal(IamPermissions.All.Count, await db.RolePermissions.CountAsync());
            Assert.Equal(2, (await db.AuthorizationStates.SingleAsync()).RbacVersion);
        });
    }

    [SkippableFact]
    public async Task Register_CreatesUnprivilegedUnconfirmedUser()
    {
        RequireContainers();
        await using var host = await StartHostAsync();

        var registered = await host.RegisterAsync("alice", "alice@permixa.test", TestKeys.UserPassword);
        Assert.False(registered.EmailConfirmed);
        Assert.Equal("alice", registered.UserName);
        Assert.Equal("alice@permixa.test", registered.Email);

        await host.ExecuteScopedAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(registered.UserId.ToString());
            Assert.NotNull(user);
            Assert.False(user!.EmailConfirmed);
            Assert.False(string.IsNullOrWhiteSpace(user.PasswordHash));
            Assert.NotEqual(TestKeys.UserPassword, user.PasswordHash);
            Assert.Empty(await users.GetRolesAsync(user));
        });
    }

    [SkippableFact]
    public async Task DuplicateRegistration_ReturnsConflict()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.RegisterAsync("alice", "alice@permixa.test", TestKeys.UserPassword);

        var duplicate = await host.RegisterRawAsync("alice2", "alice@permixa.test", TestKeys.UserPassword);
        await ProblemJson.ReadAndAssertAsync(duplicate, 409, AuthenticationErrors.EmailAlreadyExists.Code);
    }

    [SkippableFact]
    public async Task Login_Unconfirmed_AllowedWhenRequireConfirmedEmailIsFalse()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.RegisterAsync("alice", "alice@permixa.test", TestKeys.UserPassword);

        var login = await host.LoginAsync("alice@permixa.test", TestKeys.UserPassword);
        Assert.False(string.IsNullOrWhiteSpace(login.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(login.RefreshToken));
    }

    [SkippableFact]
    public async Task Login_Unconfirmed_BlockedWhenRequireConfirmedEmailIsTrue()
    {
        RequireContainers();
        await using var host = await StartHostAsync(o => o.RequireConfirmedEmail = true);
        await host.RegisterAsync("alice", "alice@permixa.test", TestKeys.UserPassword);

        var response = await host.LoginRawAsync("alice@permixa.test", TestKeys.UserPassword);
        await ProblemJson.ReadAndAssertAsync(response, 401, AuthenticationErrors.EmailNotConfirmed.Code);
    }

    [SkippableFact]
    public async Task UnknownUserAndWrongPassword_AreIndistinguishable()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.RegisterAsync("alice", "alice@permixa.test", TestKeys.UserPassword);

        var unknown = await host.LoginRawAsync("missing@permixa.test", TestKeys.UserPassword);
        var wrong = await host.LoginRawAsync("alice@permixa.test", "WrongPass1!");

        var unknownProblem = await ProblemJson.ReadAndAssertAsync(
            unknown, 401, AuthenticationErrors.InvalidCredentials.Code);
        var wrongProblem = await ProblemJson.ReadAndAssertAsync(
            wrong, 401, AuthenticationErrors.InvalidCredentials.Code);

        Assert.Equal(unknownProblem.GetProperty("detail").GetString(), wrongProblem.GetProperty("detail").GetString());
        Assert.Equal(unknown.StatusCode, wrong.StatusCode);
    }
}
