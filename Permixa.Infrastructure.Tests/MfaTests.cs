using System.IdentityModel.Tokens.Jwt;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Mfa.BeginSetup;
using Permixa.Application.Authentication.Mfa.Complete;
using Permixa.Application.Authentication.Mfa.Disable;
using Permixa.Application.Authentication.Mfa.Enable;
using Permixa.Application.Authentication.Mfa.GetMfaStatus;
using Permixa.Application.Authentication.Mfa.Regenerate;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Domain.Authentication;
using Permixa.Infrastructure;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Permixa.Infrastructure.Persistence.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.Infrastructure.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class MfaTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;

    public MfaTests(SqlServerContainerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    public Task InitializeAsync()
    {
        _connectionString = _sqlServer.CreateUniqueDatabaseConnectionString();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_AddsMfaLoginChallenges_WithNonFilteredUserIdUnique()
    {
        await using var sp = BuildProvider();
        await MigrateAsync(sp);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var columns = connection.CreateCommand();
        columns.CommandText = """
            SELECT c.name
            FROM sys.columns c
            INNER JOIN sys.tables t ON c.object_id = t.object_id
            WHERE t.name = N'MfaLoginChallenges'
            """;
        var names = new List<string>();
        await using (var reader = await columns.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));
        }

        Assert.Contains("UserId", names);
        Assert.Contains("ProofHash", names);
        Assert.Contains("AttemptCount", names);
        Assert.Contains("ConsumedAtUtc", names);

        await using var indexes = connection.CreateCommand();
        indexes.CommandText = """
            SELECT i.name, i.has_filter, i.is_unique, i.filter_definition
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            WHERE t.name = N'MfaLoginChallenges'
              AND i.name IN (N'IX_MfaLoginChallenges_UserId', N'IX_MfaLoginChallenges_ProofHash')
            """;
        var found = 0;
        await using var indexReader = await indexes.ExecuteReaderAsync();
        while (await indexReader.ReadAsync())
        {
            found++;
            var name = indexReader.GetString(0);
            Assert.True(indexReader.GetBoolean(2), name);
            var filter = indexReader.IsDBNull(3) ? null : indexReader.GetString(3);
            Assert.True(
                string.IsNullOrEmpty(filter)
                || filter == "([UserId] IS NOT NULL)"
                || filter == "([ProofHash] IS NOT NULL)",
                $"{name} unexpected filter: {filter}");
            Assert.DoesNotContain("ConsumedAtUtc", filter ?? string.Empty, StringComparison.Ordinal);
        }

        Assert.Equal(2, found);
    }

    [Fact]
    public async Task Login_MfaEnabled_DoesNotIssueTokensOrRefreshRow()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.login", "mfa.login@permixa.test");
        await EnableMfaAsync(sp, userId);

        var login = await LoginRawAsync(sp, "mfa.login@permixa.test", "Passw0rd!");
        Assert.True(login.IsSuccess);
        Assert.True(login.Value.IsMfaRequired);
        Assert.Null(login.Value.Authentication);
        Assert.False(string.IsNullOrWhiteSpace(login.Value.Mfa!.MfaProof));

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.UserId == userId));
        Assert.Equal(1, await db.MfaLoginChallenges.CountAsync(c => c.UserId == userId));
        var stored = await db.MfaLoginChallenges.SingleAsync(c => c.UserId == userId);
        Assert.Equal(crypto.HashToken(login.Value.Mfa.MfaProof), stored.ProofHash);
        Assert.DoesNotContain(login.Value.Mfa.MfaProof, stored.ProofHash);
    }

    [Fact]
    public async Task WrongPassword_DoesNotCreateChallenge()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.wrong", "mfa.wrong@permixa.test");
        await EnableMfaAsync(sp, userId);

        var login = await LoginRawAsync(sp, "mfa.wrong@permixa.test", "WrongPass1!");
        Assert.Equal(AuthenticationErrors.InvalidCredentials.Code, login.Error!.Code);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.MfaLoginChallenges.CountAsync(c => c.UserId == userId));
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.UserId == userId));
    }

    [Fact]
    public async Task Enable_RevokesPasswordSessions_AndDisableResetsKeyCodesSessions()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.en", "mfa.en@permixa.test");
        var first = await LoginAuthenticatedAsync(sp, "mfa.en@permixa.test", "Passw0rd!");
        var codes = await EnableMfaAsync(sp, userId);
        Assert.NotEmpty(codes);

        Assert.Equal(
            AuthenticationErrors.RefreshTokenRevoked.Code,
            (await RefreshRawAsync(sp, first.RefreshToken)).Error!.Code);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAtUtc == null));
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(userId.ToString());
            Assert.True(user!.TwoFactorEnabled);
            Assert.Equal(ApplicationUser.InitialAuthorizationVersion, user.AuthorizationVersion);
        }

        var pending = await LoginRawAsync(sp, "mfa.en@permixa.test", "Passw0rd!");
        Assert.True(pending.Value.IsMfaRequired);

        using (var scope = sp.CreateScope())
        {
            var disabled = await scope.ServiceProvider.GetRequiredService<DisableMfaUseCase>()
                .ExecuteAsync(new DisableMfaCommand(userId, "Passw0rd!"));
            Assert.True(disabled.IsSuccess, disabled.Error?.Description);
        }

        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            Assert.False(user!.TwoFactorEnabled);
            Assert.False(string.IsNullOrEmpty(await users.GetAuthenticatorKeyAsync(user)));
            Assert.Equal(0, await users.CountRecoveryCodesAsync(user));
            Assert.Equal(ApplicationUser.InitialAuthorizationVersion, user.AuthorizationVersion);
        }

        var afterDisable = await LoginRawAsync(sp, "mfa.en@permixa.test", "Passw0rd!");
        Assert.True(afterDisable.Value.IsAuthenticated);
        Assert.False(string.IsNullOrWhiteSpace(afterDisable.Value.Authentication!.AccessToken));
    }

    [Fact]
    public async Task CompleteTotp_IssuesTokens_AndReuseFails()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.totp", "mfa.totp@permixa.test");
        await EnableMfaAsync(sp, userId);
        var pending = await LoginRawAsync(sp, "mfa.totp@permixa.test", "Passw0rd!");

        var totp = await GenerateTotpAsync(sp, userId);
        AuthenticationResult? issued;
        using (var scope = sp.CreateScope())
        {
            var completed = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
                .ExecuteAsync(new CompleteMfaWithTotpRequest
                {
                    MfaProof = pending.Value.Mfa!.MfaProof,
                    TotpCode = totp
                });
            Assert.True(completed.IsSuccess, completed.Error?.Description);
            issued = completed.Value;
        }

        AssertTokensDoNotContainForbiddenClaims(issued!.AccessToken);

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAtUtc == null));
            var challenge = await db.MfaLoginChallenges.SingleAsync(c => c.UserId == userId);
            Assert.True(challenge.IsConsumed);
        }

        using (var scope = sp.CreateScope())
        {
            var reuse = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
                .ExecuteAsync(new CompleteMfaWithTotpRequest
                {
                    MfaProof = pending.Value.Mfa!.MfaProof,
                    TotpCode = totp
                });
            Assert.Equal(MfaErrors.ChallengeInvalid.Code, reuse.Error!.Code);
        }
    }

    [Fact]
    public async Task CompleteRecovery_ThenCodeReuseFails_AndRegenerateDoesNotRevoke()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.rec", "mfa.rec@permixa.test");
        var codes = await EnableMfaAsync(sp, userId);
        var pending = await LoginRawAsync(sp, "mfa.rec@permixa.test", "Passw0rd!");

        using (var scope = sp.CreateScope())
        {
            var completed = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithRecoveryCodeUseCase>()
                .ExecuteAsync(new CompleteMfaWithRecoveryCodeRequest
                {
                    MfaProof = pending.Value.Mfa!.MfaProof,
                    RecoveryCode = codes[0]
                });
            Assert.True(completed.IsSuccess, completed.Error?.Description);
        }

        var again = await LoginRawAsync(sp, "mfa.rec@permixa.test", "Passw0rd!");
        using (var scope = sp.CreateScope())
        {
            var reuse = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithRecoveryCodeUseCase>()
                .ExecuteAsync(new CompleteMfaWithRecoveryCodeRequest
                {
                    MfaProof = again.Value.Mfa!.MfaProof,
                    RecoveryCode = codes[0]
                });
            Assert.Equal(MfaErrors.CodeInvalid.Code, reuse.Error!.Code);
        }

        Guid family;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            family = (await db.RefreshTokens.SingleAsync(t => t.UserId == userId && t.RevokedAtUtc == null)).FamilyId;
        }

        using (var scope = sp.CreateScope())
        {
            var regenerated = await scope.ServiceProvider.GetRequiredService<RegenerateRecoveryCodesUseCase>()
                .ExecuteAsync(new RegenerateRecoveryCodesCommand(userId, "Passw0rd!"));
            Assert.True(regenerated.IsSuccess, regenerated.Error?.Description);
            Assert.NotEqual(codes[0], regenerated.Value.RecoveryCodes[0]);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.RefreshTokens.CountAsync(t =>
                t.UserId == userId && t.FamilyId == family && t.RevokedAtUtc == null));
        }
    }

    [Fact]
    public async Task InvalidTotp_IncrementsChallengeOnly_NotAccessFailedCount()
    {
        await using var sp = BuildProvider(o => o.Mfa.MaxAttempts = 3);
        var userId = await CreateUserAsync(sp, "mfa.att", "mfa.att@permixa.test");
        await EnableMfaAsync(sp, userId);
        var pending = await LoginRawAsync(sp, "mfa.att@permixa.test", "Passw0rd!");

        for (var i = 0; i < 2; i++)
        {
            using var scope = sp.CreateScope();
            var failed = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
                .ExecuteAsync(new CompleteMfaWithTotpRequest
                {
                    MfaProof = pending.Value.Mfa!.MfaProof,
                    TotpCode = "000000"
                });
            Assert.Equal(MfaErrors.CodeInvalid.Code, failed.Error!.Code);
        }

        using (var scope = sp.CreateScope())
        {
            var locked = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
                .ExecuteAsync(new CompleteMfaWithTotpRequest
                {
                    MfaProof = pending.Value.Mfa!.MfaProof,
                    TotpCode = "000000"
                });
            Assert.Equal(MfaErrors.AttemptsExceeded.Code, locked.Error!.Code);

            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            Assert.Equal(0, user!.AccessFailedCount);
            var challenge = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
                .MfaLoginChallenges.AsNoTracking()
                .SingleAsync(c => c.UserId == userId);
            Assert.True(challenge.IsConsumed);
            Assert.Equal(3, challenge.AttemptCount);
        }
    }

    [Fact]
    public async Task ExpiredAndUnknownProof_AreChallengeInvalid()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.exp", "mfa.exp@permixa.test");
        await EnableMfaAsync(sp, userId);
        var pending = await LoginRawAsync(sp, "mfa.exp@permixa.test", "Passw0rd!");

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var challenge = await db.MfaLoginChallenges.SingleAsync(c => c.UserId == userId);
            challenge.Replace(
                challenge.ProofHash,
                DateTime.UtcNow.AddMinutes(-10),
                DateTime.UtcNow.AddMinutes(-1));
            await db.SaveChangesAsync();
        }

        using (var scope = sp.CreateScope())
        {
            var expired = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
                .ExecuteAsync(new CompleteMfaWithTotpRequest
                {
                    MfaProof = pending.Value.Mfa!.MfaProof,
                    TotpCode = await GenerateTotpAsync(sp, userId)
                });
            Assert.Equal(MfaErrors.ChallengeInvalid.Code, expired.Error!.Code);

            var missing = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
                .ExecuteAsync(new CompleteMfaWithTotpRequest
                {
                    MfaProof = "not-a-real-proof",
                    TotpCode = "123456"
                });
            Assert.Equal(MfaErrors.ChallengeInvalid.Code, missing.Error!.Code);
        }
    }

    [Fact]
    public async Task MidChallenge_RechecksAccountGates()
    {
        await using var sp = BuildProvider(o => o.Authentication.RequireConfirmedEmail = true);
        var userId = await CreateUserAsync(sp, "mfa.gate", "mfa.gate@permixa.test", confirmEmail: true);
        await EnableMfaAsync(sp, userId);
        var pending = await LoginRawAsync(sp, "mfa.gate@permixa.test", "Passw0rd!");
        var totp = await GenerateTotpAsync(sp, userId);

        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            user!.EmailConfirmed = false;
            Assert.Equal(IdentityResult.Success, await users.UpdateAsync(user));
        }

        using (var scope = sp.CreateScope())
        {
            var email = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
                .ExecuteAsync(new CompleteMfaWithTotpRequest
                {
                    MfaProof = pending.Value.Mfa!.MfaProof,
                    TotpCode = totp
                });
            Assert.Equal(AuthenticationErrors.EmailNotConfirmed.Code, email.Error!.Code);
        }

        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            user!.EmailConfirmed = true;
            Assert.Equal(IdentityResult.Success, await users.UpdateAsync(user));
            Assert.Equal(IdentityResult.Success, await users.SetLockoutEnabledAsync(user, true));
            Assert.Equal(IdentityResult.Success, await users.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddHours(1)));
        }

        using (var scope = sp.CreateScope())
        {
            var locked = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
                .ExecuteAsync(new CompleteMfaWithTotpRequest
                {
                    MfaProof = pending.Value.Mfa!.MfaProof,
                    TotpCode = totp
                });
            Assert.Equal(AuthenticationErrors.LockedOut.Code, locked.Error!.Code);
        }

        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            Assert.Equal(IdentityResult.Success, await users.SetLockoutEndDateAsync(user!, null));
            user!.SetDisabled(true);
            Assert.Equal(IdentityResult.Success, await users.UpdateAsync(user));
        }

        using (var scope = sp.CreateScope())
        {
            var disabled = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
                .ExecuteAsync(new CompleteMfaWithTotpRequest
                {
                    MfaProof = pending.Value.Mfa!.MfaProof,
                    TotpCode = totp
                });
            Assert.Equal(AuthenticationErrors.InvalidCredentials.Code, disabled.Error!.Code);
        }
    }

    [Fact]
    public async Task MidChallenge_MfaDisabled_ReturnsNotEnabled()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.off", "mfa.off@permixa.test");
        await EnableMfaAsync(sp, userId);
        var pending = await LoginRawAsync(sp, "mfa.off@permixa.test", "Passw0rd!");

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<DisableMfaUseCase>()
                .ExecuteAsync(new DisableMfaCommand(userId, "Passw0rd!"))).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
                .ExecuteAsync(new CompleteMfaWithTotpRequest
                {
                    MfaProof = pending.Value.Mfa!.MfaProof,
                    TotpCode = "123456"
                });
            Assert.Equal(MfaErrors.NotEnabled.Code, result.Error!.Code);
        }
    }

    [Fact]
    public async Task ConcurrentTotp_SameProof_AuthenticatesOnce()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.race", "mfa.race@permixa.test");
        await EnableMfaAsync(sp, userId);
        var pending = await LoginRawAsync(sp, "mfa.race@permixa.test", "Passw0rd!");
        var totp = await GenerateTotpAsync(sp, userId);

        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();
        var t1 = scope1.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
            .ExecuteAsync(new CompleteMfaWithTotpRequest
            {
                MfaProof = pending.Value.Mfa!.MfaProof,
                TotpCode = totp
            });
        var t2 = scope2.ServiceProvider.GetRequiredService<CompleteMfaWithTotpUseCase>()
            .ExecuteAsync(new CompleteMfaWithTotpRequest
            {
                MfaProof = pending.Value.Mfa.MfaProof,
                TotpCode = totp
            });
        await Task.WhenAll(t1, t2);

        var successes = new[] { t1.Result, t2.Result }.Count(r => r.IsSuccess);
        Assert.Equal(1, successes);
        Assert.Contains(new[] { t1.Result, t2.Result }, r => r.Error?.Code == MfaErrors.ChallengeInvalid.Code);

        using var verify = sp.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAtUtc == null));
    }

    [Fact]
    public async Task ConcurrentPasswordLogins_LeaveOneChallengeRow()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.one", "mfa.one@permixa.test");
        await EnableMfaAsync(sp, userId);

        using var scope1 = sp.CreateScope();
        using var scope2 = sp.CreateScope();
        var t1 = scope1.ServiceProvider.GetRequiredService<LoginUseCase>()
            .ExecuteAsync(new LoginRequest { EmailOrUserName = "mfa.one@permixa.test", Password = "Passw0rd!" });
        var t2 = scope2.ServiceProvider.GetRequiredService<LoginUseCase>()
            .ExecuteAsync(new LoginRequest { EmailOrUserName = "mfa.one@permixa.test", Password = "Passw0rd!" });
        await Task.WhenAll(t1, t2);

        Assert.True(t1.Result.IsSuccess);
        Assert.True(t2.Result.IsSuccess);
        Assert.True(t1.Result.Value.IsMfaRequired);
        Assert.True(t2.Result.Value.IsMfaRequired);

        using var verify = sp.CreateScope();
        var db = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(1, await db.MfaLoginChallenges.CountAsync(c => c.UserId == userId));
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t => t.UserId == userId));
    }

    [Fact]
    public async Task Enable_TransactionFailure_LeavesMfaOff_AndSessionsActive()
    {
        await using var sp = BuildProvider(throwAfterRevoke: true);
        var userId = await CreateUserAsync(sp, "mfa.tx", "mfa.tx@permixa.test");
        var login = await LoginAuthenticatedAsync(sp, "mfa.tx@permixa.test", "Passw0rd!");

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<BeginAuthenticatorSetupUseCase>()
                .ExecuteAsync(new BeginAuthenticatorSetupCommand(userId));
            var totp = await GenerateTotpAsync(sp, userId);
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<EnableAuthenticatorMfaUseCase>()
                    .ExecuteAsync(new EnableAuthenticatorMfaCommand(userId, totp)));
            Assert.Contains("Forced failure", ex.Message, StringComparison.Ordinal);
        }

        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            Assert.False(user!.TwoFactorEnabled);
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAtUtc == null));
        }

        var refresh = await RefreshRawAsync(sp, login.RefreshToken);
        Assert.True(refresh.IsSuccess, refresh.Error?.Description);
    }

    [Fact]
    public async Task EnableAndDisable_DoNotBlacklistJwt_OrBumpVersions()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.jwt", "mfa.jwt@permixa.test");
        var login = await LoginAuthenticatedAsync(sp, "mfa.jwt@permixa.test", "Passw0rd!");
        await EnableMfaAsync(sp, userId);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "AuthorizationVersion");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "RbacVersion");
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "SecurityStamp");

        using var scope = sp.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(userId.ToString());
        Assert.NotNull(user);
        Assert.Equal(ApplicationUser.InitialAuthorizationVersion, user.AuthorizationVersion);
        var rbac = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .AuthorizationStates.Select(s => (int?)s.RbacVersion).SingleOrDefaultAsync();
        Assert.True(rbac is null or 0);
    }

    [Fact]
    public async Task SecondChallengeRow_IsRejectedByUserIdUnique()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.uniq", "mfa.uniq@permixa.test");

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        db.MfaLoginChallenges.Add(MfaLoginChallenge.Create(userId, new string('a', 64), now.AddMinutes(5), createdAtUtc: now));
        await db.SaveChangesAsync();
        db.MfaLoginChallenges.Add(MfaLoginChallenge.Create(userId, new string('b', 64), now.AddMinutes(5), createdAtUtc: now));
        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(new SqlServerPersistenceExceptionClassifier().IsUniqueConstraintViolation(ex));
    }

    [Fact]
    public async Task Status_DoesNotReturnRecoveryCodes()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAsync(sp, "mfa.st", "mfa.st@permixa.test");
        await EnableMfaAsync(sp, userId);

        using var scope = sp.CreateScope();
        var status = await scope.ServiceProvider.GetRequiredService<GetMfaStatusUseCase>()
            .ExecuteAsync(new GetMfaStatusQuery(userId));
        Assert.True(status.IsSuccess);
        Assert.True(status.Value.IsEnabled);
        Assert.True(status.Value.HasAuthenticatorKey);
        Assert.Equal(10, status.Value.RecoveryCodesRemaining);
    }

    private ServiceProvider BuildProvider(
        Action<PermixaAuthenticationRegistrationOptions>? configure = null,
        bool throwAfterRevoke = false)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = _connectionString;
        });
        services.AddPermixaAuthentication(o =>
        {
            o.Jwt.Issuer = TestJwtKeys.Issuer;
            o.Jwt.Audience = TestJwtKeys.Audience;
            o.Jwt.PrivateKeyPem = TestJwtKeys.PrivateKeyPem;
            o.Jwt.AccessTokenLifetime = TimeSpan.FromMinutes(15);
            o.Authentication.RefreshTokenLifetime = TimeSpan.FromDays(7);
            configure?.Invoke(o);
        });

        if (throwAfterRevoke)
        {
            var existing = services.Single(d => d.ServiceType == typeof(IRefreshTokenRepository));
            services.Remove(existing);
            services.AddScoped<RefreshTokenRepository>();
            services.AddScoped<IRefreshTokenRepository>(sp =>
                new ThrowAfterRevokeRefreshTokenRepository(sp.GetRequiredService<RefreshTokenRepository>()));
        }

        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    private static async Task<Guid> CreateUserAsync(
        ServiceProvider sp,
        string userName,
        string email,
        bool confirmEmail = false)
    {
        await MigrateAsync(sp);
        using var scope = sp.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser(userName) { Email = email, EmailConfirmed = confirmEmail };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        return user.Id;
    }

    private static async Task<IReadOnlyList<string>> EnableMfaAsync(ServiceProvider sp, Guid userId)
    {
        using var scope = sp.CreateScope();
        var began = await scope.ServiceProvider.GetRequiredService<BeginAuthenticatorSetupUseCase>()
            .ExecuteAsync(new BeginAuthenticatorSetupCommand(userId));
        Assert.True(began.IsSuccess, began.Error?.Description);
        var totp = await GenerateTotpAsync(sp, userId);
        var enabled = await scope.ServiceProvider.GetRequiredService<EnableAuthenticatorMfaUseCase>()
            .ExecuteAsync(new EnableAuthenticatorMfaCommand(userId, totp));
        Assert.True(enabled.IsSuccess, enabled.Error?.Description);
        return enabled.Value.RecoveryCodes;
    }

    private static async Task<string> GenerateTotpAsync(ServiceProvider sp, Guid userId)
    {
        using var scope = sp.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        return await IdentityTotpTestHelper.GenerateAsync(users, userId);
    }

    private static async Task<Permixa.Application.Common.Results.Result<LoginResult>> LoginRawAsync(
        ServiceProvider sp,
        string email,
        string password)
    {
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LoginUseCase>()
            .ExecuteAsync(new LoginRequest { EmailOrUserName = email, Password = password });
    }

    private static async Task<AuthenticationResult> LoginAuthenticatedAsync(
        ServiceProvider sp,
        string email,
        string password)
    {
        var result = await LoginRawAsync(sp, email, password);
        Assert.True(result.IsSuccess, result.Error?.Description);
        Assert.True(result.Value.IsAuthenticated);
        return result.Value.Authentication!;
    }

    private static async Task<Permixa.Application.Common.Results.Result<AuthenticationResult>> RefreshRawAsync(
        ServiceProvider sp,
        string refreshToken)
    {
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
            .ExecuteAsync(new RefreshTokenRequest { RefreshToken = refreshToken });
    }

    private static void AssertTokensDoNotContainForbiddenClaims(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var types = jwt.Claims.Select(c => c.Type).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("AuthorizationVersion", types);
        Assert.DoesNotContain("RbacVersion", types);
        Assert.DoesNotContain("SecurityStamp", types);
    }

    private sealed class ThrowAfterRevokeRefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly IRefreshTokenRepository _inner;

        public ThrowAfterRevokeRefreshTokenRepository(IRefreshTokenRepository inner)
        {
            _inner = inner;
        }

        public Task<RefreshToken?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default) =>
            _inner.GetByTokenHashAsync(tokenHash, cancellationToken);

        public Task<RefreshToken?> GetByIdAsync(Guid refreshTokenId, CancellationToken cancellationToken = default) =>
            _inner.GetByIdAsync(refreshTokenId, cancellationToken);

        public Task<IReadOnlyList<RefreshToken>> GetActiveByFamilyIdAsync(
            Guid familyId,
            CancellationToken cancellationToken = default) =>
            _inner.GetActiveByFamilyIdAsync(familyId, cancellationToken);

        public Task AddAsync(RefreshToken refreshToken, CancellationToken cancellationToken = default) =>
            _inner.AddAsync(refreshToken, cancellationToken);

        public async Task<int> RevokeAllForUserAsync(
            Guid userId,
            DateTime revokedAtUtc,
            CancellationToken cancellationToken = default)
        {
            await _inner.RevokeAllForUserAsync(userId, revokedAtUtc, cancellationToken);
            throw new InvalidOperationException("Forced failure after refresh-token revocation.");
        }

        public Task<bool> FamilyExistsForUserAsync(
            Guid userId,
            Guid familyId,
            CancellationToken cancellationToken = default) =>
            _inner.FamilyExistsForUserAsync(userId, familyId, cancellationToken);

        public Task<int> RevokeFamilyForUserAsync(
            Guid userId,
            Guid familyId,
            DateTime revokedAtUtc,
            CancellationToken cancellationToken = default) =>
            _inner.RevokeFamilyForUserAsync(userId, familyId, revokedAtUtc, cancellationToken);

        public Task<bool> HasActiveFamilyForUserAsync(
            Guid userId,
            Guid familyId,
            DateTime utcNow,
            CancellationToken cancellationToken = default) =>
            _inner.HasActiveFamilyForUserAsync(userId, familyId, utcNow, cancellationToken);

        public Task<int> RevokeAllForUserExceptFamilyAsync(
            Guid userId,
            Guid currentFamilyId,
            DateTime revokedAtUtc,
            CancellationToken cancellationToken = default) =>
            _inner.RevokeAllForUserExceptFamilyAsync(userId, currentFamilyId, revokedAtUtc, cancellationToken);
    }
}
