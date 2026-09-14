using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Json;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.ChangePassword;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Roles.Create;
using Permixa.Application.Authorization.UserRoles.Assign;
using Permixa.Application.Authorization.Users.ChangeEmail;
using Permixa.Application.Authorization.Users.Create;
using Permixa.Application.Authorization.Users.ForcePasswordReset;
using Permixa.Application.Verification.EmailChange;
using Permixa.Application.Verification.PasswordReset;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Permixa.IntegrationTests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.IntegrationTests;

public sealed class CredentialsAndEmailChangeFlowTests : IntegrationTestBase
{
    public CredentialsAndEmailChangeFlowTests(SqlServerContainerFixture sql, RedisContainerFixture redis)
        : base(sql, redis)
    {
    }

    [SkippableFact]
    public async Task ChangePasswordEmailChangeAndForceReset_FollowApprovedSecuritySemantics()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.BootstrapAsync();

        var registered = await host.RegisterAsync("cred.flow", "cred.flow@permixa.test", TestKeys.UserPassword);
        var loginA = await host.LoginAsync("cred.flow@permixa.test", TestKeys.UserPassword);
        var loginB = await host.LoginAsync("cred.flow@permixa.test", TestKeys.UserPassword);
        var familyA = await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var crypto = sp.GetRequiredService<Permixa.Application.Authentication.Abstractions.IRefreshTokenCrypto>();
            return (await db.RefreshTokens.SingleAsync(t =>
                t.TokenHash == crypto.HashToken(loginA.RefreshToken))).FamilyId;
        });

        var changed = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<ChangePasswordUseCase>().ExecuteAsync(new ChangePasswordRequest
            {
                UserId = registered.UserId,
                CurrentPassword = TestKeys.UserPassword,
                NewPassword = "N3wPassw0rd!",
                CurrentFamilyId = familyA
            }));
        Assert.True(changed.IsSuccess, changed.Error?.Description);
        Assert.False(changed.Value.ReauthenticationRequired);

        var kept = await host.Client.PostAsJsonAsync(
            "/refresh", new RefreshTokenRequest { RefreshToken = loginA.RefreshToken });
        kept.EnsureSuccessStatusCode();
        await ProblemJson.ReadAndAssertAsync(
            await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest { RefreshToken = loginB.RefreshToken }),
            401,
            AuthenticationErrors.RefreshTokenRevoked.Code);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(loginA.AccessToken);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);

        var requested = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<RequestEmailChangeUseCase>().ExecuteAsync(new RequestEmailChangeRequest
            {
                UserId = registered.UserId,
                CurrentPassword = "N3wPassw0rd!",
                NewEmail = "cred.pending@permixa.test"
            }));
        Assert.True(requested.IsSuccess, requested.Error?.Description);
        Assert.Equal("cred.pending@permixa.test", host.Emails.Last.To);

        var (challengeId, token, _) = host.Emails.ExtractLink("Confirm your email address by opening this link:");

        var confirmed = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<ConfirmEmailChangeUseCase>().ExecuteAsync(new ConfirmEmailChangeRequest
            {
                ChallengeId = challengeId,
                Token = token
            }));
        Assert.True(confirmed.IsSuccess, confirmed.Error?.Description);

        await host.ExecuteScopedAsync(async sp =>
        {
            var user = await sp.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(registered.UserId.ToString());
            Assert.Equal("cred.pending@permixa.test", user!.Email);
            Assert.True(user.EmailConfirmed);
            Assert.Null(user.PendingEmail);
        });

        var owner = await host.LoginAsync(TestKeys.OwnerEmail, TestKeys.OwnerPassword);
        var ownerRoleId = await host.ExecuteScopedAsync(async sp =>
            (await sp.GetRequiredService<ApplicationDbContext>().Roles
                .SingleAsync(r => r.Name == SystemRoles.Owner)).Id);
        var adminRole = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<CreateRoleUseCase>().ExecuteAsync(
                new CreateRoleCommand(owner.UserId, "CredAdmin", ownerRoleId, RolePlacement.Below)));
        Assert.True(adminRole.IsSuccess, adminRole.Error?.Description);
        var adminUser = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<AdminCreateUserUseCase>().ExecuteAsync(
                new AdminCreateUserCommand(
                    owner.UserId, "cred.admin@permixa.test", "cred.admin", TestKeys.UserPassword)));
        Assert.True(adminUser.IsSuccess, adminUser.Error?.Description);
        Assert.True((await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<AssignRoleToUserUseCase>().ExecuteAsync(
                new AssignRoleToUserCommand(owner.UserId, adminUser.Value.Id, adminRole.Value.Id)))).IsSuccess);

        await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            foreach (var name in new[] { IamPermissions.Users.ChangeEmail, IamPermissions.Users.ForcePasswordReset })
            {
                var permissionId = (await db.Permissions.SingleAsync(p => p.Name == name)).Id;
                await host.AssignPermissionToRoleAsync(owner.UserId, adminRole.Value.Id, permissionId);
            }
        });

        var target = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<AdminCreateUserUseCase>().ExecuteAsync(
                new AdminCreateUserCommand(
                    owner.UserId, "cred.target@permixa.test", "cred.target", TestKeys.UserPassword)));
        Assert.True(target.IsSuccess, target.Error?.Description);
        var targetLogin = await host.LoginAsync("cred.target@permixa.test", TestKeys.UserPassword);

        Assert.True((await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<AdminRequestEmailChangeUseCase>().ExecuteAsync(
                new AdminRequestEmailChangeCommand(
                    adminUser.Value.Id, target.Value.Id, "cred.target2@permixa.test")))).IsSuccess);
        await host.ExecuteScopedAsync(async sp =>
        {
            var user = await sp.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(target.Value.Id.ToString());
            Assert.Equal("cred.target@permixa.test", user!.Email);
            Assert.Equal("cred.target2@permixa.test", user.PendingEmail);
        });

        Assert.True((await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<ForcePasswordResetUseCase>().ExecuteAsync(
                new ForcePasswordResetCommand(adminUser.Value.Id, target.Value.Id)))).IsSuccess);

        await ProblemJson.ReadAndAssertAsync(
            await host.Client.PostAsJsonAsync("/refresh", new RefreshTokenRequest { RefreshToken = targetLogin.RefreshToken }),
            401,
            AuthenticationErrors.RefreshTokenRevoked.Code);

        Assert.Equal("cred.target@permixa.test", host.Emails.Last.To);
        var resetLink = host.Emails.ExtractLink("Reset your password by opening this link:");
        var reset = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<ResetPasswordWithVerificationUseCase>().ExecuteAsync(
                new ResetPasswordWithVerificationRequest
                {
                    ChallengeId = resetLink.ChallengeId,
                    Token = resetLink.Token,
                    NewPassword = "TargetN3w!"
                }));
        Assert.True(reset.IsSuccess, reset.Error?.Description);
        var relogin = await host.LoginAsync("cred.target@permixa.test", "TargetN3w!");
        Assert.False(string.IsNullOrWhiteSpace(relogin.AccessToken));
    }
}
