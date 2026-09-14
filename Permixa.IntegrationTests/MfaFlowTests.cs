using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Mfa.BeginSetup;
using Permixa.Application.Authentication.Mfa.Complete;
using Permixa.Application.Authentication.Mfa.Disable;
using Permixa.Application.Authentication.Mfa.Enable;
using Permixa.Application.Authentication.Mfa.Regenerate;
using Permixa.Application.Authentication.Refresh;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Permixa.IntegrationTests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.IntegrationTests;

public sealed class MfaFlowTests : IntegrationTestBase
{
    public MfaFlowTests(SqlServerContainerFixture sql, RedisContainerFixture redis)
        : base(sql, redis)
    {
    }

    [SkippableFact]
    public async Task EnableLoginTotpRecoveryRegenerateAndDisable_FollowApprovedSecuritySemantics()
    {
        RequireContainers();
        await using var host = await StartHostAsync();

        var registered = await host.RegisterAsync("mfa.flow", "mfa.flow@permixa.test", TestKeys.UserPassword);
        var passwordSession = await host.LoginAsync("mfa.flow@permixa.test", TestKeys.UserPassword);

        IReadOnlyList<string> codes = Array.Empty<string>();
        await host.ExecuteScopedAsync(async sp =>
        {
            var began = await sp.GetRequiredService<BeginAuthenticatorSetupUseCase>()
                .ExecuteAsync(new BeginAuthenticatorSetupCommand(registered.UserId));
            Assert.True(began.IsSuccess, began.Error?.Description);

            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var totp = await IdentityTotpTestHelper.GenerateAsync(users, registered.UserId);

            var enabled = await sp.GetRequiredService<EnableAuthenticatorMfaUseCase>()
                .ExecuteAsync(new EnableAuthenticatorMfaCommand(registered.UserId, totp));
            Assert.True(enabled.IsSuccess, enabled.Error?.Description);
            codes = enabled.Value.RecoveryCodes;
        });

        Assert.Equal(
            AuthenticationErrors.RefreshTokenRevoked.Code,
            (await host.RefreshRawAsync(passwordSession.RefreshToken)).Error!.Code);

        var stillValidAccess = await host.AuthenticatedClient(passwordSession.AccessToken).GetAsync("/me");
        stillValidAccess.EnsureSuccessStatusCode();

        var mfaLogin = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<LoginUseCase>()
                .ExecuteAsync(new LoginRequest
                {
                    EmailOrUserName = "mfa.flow@permixa.test",
                    Password = TestKeys.UserPassword
                }));
        Assert.True(mfaLogin.IsSuccess, mfaLogin.Error?.Description);
        Assert.True(mfaLogin.Value.IsMfaRequired);

        await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.RefreshTokens.CountAsync(t =>
                t.UserId == registered.UserId && t.RevokedAtUtc == null));
        });

        var totpSession = await host.ExecuteScopedAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var totp = await IdentityTotpTestHelper.GenerateAsync(users, registered.UserId);
            var completed = await sp.GetRequiredService<CompleteMfaWithTotpUseCase>()
                .ExecuteAsync(new CompleteMfaWithTotpRequest
                {
                    MfaProof = mfaLogin.Value.Mfa!.MfaProof,
                    TotpCode = totp
                });
            Assert.True(completed.IsSuccess, completed.Error?.Description);
            return completed.Value;
        });

        var recoveryLogin = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<LoginUseCase>()
                .ExecuteAsync(new LoginRequest
                {
                    EmailOrUserName = "mfa.flow@permixa.test",
                    Password = TestKeys.UserPassword
                }));
        Assert.True(recoveryLogin.Value.IsMfaRequired);

        var recoverySession = await host.ExecuteScopedAsync(async sp =>
        {
            var completed = await sp.GetRequiredService<CompleteMfaWithRecoveryCodeUseCase>()
                .ExecuteAsync(new CompleteMfaWithRecoveryCodeRequest
                {
                    MfaProof = recoveryLogin.Value.Mfa!.MfaProof,
                    RecoveryCode = codes[0]
                });
            Assert.True(completed.IsSuccess, completed.Error?.Description);
            return completed.Value;
        });

        var reuseLogin = await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<LoginUseCase>()
                .ExecuteAsync(new LoginRequest
                {
                    EmailOrUserName = "mfa.flow@permixa.test",
                    Password = TestKeys.UserPassword
                }));
        await host.ExecuteScopedAsync(async sp =>
        {
            var reuse = await sp.GetRequiredService<CompleteMfaWithRecoveryCodeUseCase>()
                .ExecuteAsync(new CompleteMfaWithRecoveryCodeRequest
                {
                    MfaProof = reuseLogin.Value.Mfa!.MfaProof,
                    RecoveryCode = codes[0]
                });
            Assert.Equal(MfaErrors.CodeInvalid, reuse.Error);
        });

        await host.ExecuteScopedAsync(async sp =>
        {
            var regenerated = await sp.GetRequiredService<RegenerateRecoveryCodesUseCase>()
                .ExecuteAsync(new RegenerateRecoveryCodesCommand(registered.UserId, TestKeys.UserPassword));
            Assert.True(regenerated.IsSuccess, regenerated.Error?.Description);
        });

        var afterRegenerate = await host.RefreshRawAsync(totpSession.RefreshToken);
        Assert.True(afterRegenerate.IsSuccess, afterRegenerate.Error?.Description);

        await host.ExecuteScopedAsync(async sp =>
        {
            var disabled = await sp.GetRequiredService<DisableMfaUseCase>()
                .ExecuteAsync(new DisableMfaCommand(registered.UserId, TestKeys.UserPassword));
            Assert.True(disabled.IsSuccess, disabled.Error?.Description);
        });

        Assert.Equal(
            AuthenticationErrors.RefreshTokenRevoked.Code,
            (await host.RefreshRawAsync(afterRegenerate.Value.RefreshToken)).Error!.Code);
        Assert.Equal(
            AuthenticationErrors.RefreshTokenRevoked.Code,
            (await host.RefreshRawAsync(recoverySession.RefreshToken)).Error!.Code);

        var passwordAgain = await host.LoginAsync("mfa.flow@permixa.test", TestKeys.UserPassword);
        Assert.False(string.IsNullOrWhiteSpace(passwordAgain.AccessToken));
    }
}

internal static class MfaFlowHostExtensions
{
    public static async Task<Permixa.Application.Common.Results.Result<Permixa.Application.Authentication.Models.AuthenticationResult>> RefreshRawAsync(
        this PermixaTestHost host,
        string refreshToken) =>
        await host.ExecuteScopedAsync(sp =>
            sp.GetRequiredService<RefreshAccessTokenUseCase>()
                .ExecuteAsync(new RefreshTokenRequest { RefreshToken = refreshToken }));
}
