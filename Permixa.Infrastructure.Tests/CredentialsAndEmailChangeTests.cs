using System.IdentityModel.Tokens.Jwt;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.ChangePassword;
using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.RolePermissions.Assign;
using Permixa.Application.Authorization.Users.ChangeEmail;
using Permixa.Application.Authorization.Users.ForcePasswordReset;
using Permixa.Application.Identity.Abstractions;
using Permixa.Application.Verification;
using Permixa.Application.Verification.Abstractions;
using Permixa.Application.Verification.EmailChange;
using Permixa.Application.Verification.PasswordReset;
using Permixa.Domain.Verification;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.Infrastructure.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class CredentialsAndEmailChangeTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;
    private CapturingVerificationDispatcher _dispatcher = null!;

    public CredentialsAndEmailChangeTests(SqlServerContainerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    public Task InitializeAsync()
    {
        _connectionString = _sqlServer.CreateUniqueDatabaseConnectionString();
        _dispatcher = new CapturingVerificationDispatcher();
        return Task.CompletedTask;
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task Migration_AddsNullablePendingEmail()
    {
        await using var sp = BuildProvider();
        await MigrateAsync(sp);

        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.CommandText = """
            SELECT c.is_nullable, ty.name, c.max_length
            FROM sys.columns c
            INNER JOIN sys.tables t ON t.object_id = c.object_id
            INNER JOIN sys.types ty ON ty.user_type_id = c.user_type_id
            WHERE t.name = N'AspNetUsers' AND c.name = N'PendingEmail'
            """;

        await using (var reader = await command.ExecuteReaderAsync())
        {
            Assert.True(await reader.ReadAsync());
            Assert.True(reader.GetBoolean(0));
            Assert.Equal("nvarchar", reader.GetString(1), StringComparer.OrdinalIgnoreCase);
            Assert.Equal(512, reader.GetInt16(2));
        }

        await db.Database.CloseConnectionAsync();

        var user = new ApplicationUser("pending.baseline") { Email = "pending.baseline@permixa.test" };
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        Assert.Null(user.PendingEmail);
    }

    [Fact]
    public async Task Bootstrap_SeedsCredentialPermissions_ForOwner_Idempotently()
    {
        await using var sp = BuildProvider();
        await MigrateBootstrapAsync(sp);
        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
        }

        using var assert = sp.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var owner = await assert.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("owner@permixa.test");
        var snapshot = await assert.ServiceProvider.GetRequiredService<Permixa.Application.Authorization.Abstractions.IEffectivePermissionService>()
            .GetAuthorizationSnapshotAsync(owner!.Id);

        Assert.Equal(1, await db.Permissions.CountAsync(p => p.Name == IamPermissions.Users.ChangeEmail));
        Assert.Equal(1, await db.Permissions.CountAsync(p => p.Name == IamPermissions.Users.ForcePasswordReset));
        Assert.Equal(IamPermissions.All.Count, await db.Permissions.CountAsync());
        Assert.Equal(2, (await db.AuthorizationStates.SingleAsync()).RbacVersion);
        Assert.True(snapshot.Value.HasPermission(IamPermissions.Users.ChangeEmail));
        Assert.True(snapshot.Value.HasPermission(IamPermissions.Users.ForcePasswordReset));
    }

    [Fact]
    public async Task ChangePassword_PreservesCurrentFamily_AndRevokesOthers()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAfterBootstrapAsync(sp, "chg.keep", "chg.keep@permixa.test");
        var first = await LoginAsync(sp, "chg.keep@permixa.test", "Passw0rd!");
        var second = await LoginAsync(sp, "chg.keep@permixa.test", "Passw0rd!");
        var familyA = await FamilyOfAsync(sp, first.RefreshToken);
        var rbacBefore = await ReadRbacFromRootAsync(sp);
        var versionBefore = await ReadAuthorizationVersionAsync(sp, userId);

        using (var scope = sp.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ChangePasswordUseCase>()
                .ExecuteAsync(new ChangePasswordRequest
                {
                    UserId = userId,
                    CurrentPassword = "Passw0rd!",
                    NewPassword = "N3wPassw0rd!",
                    CurrentFamilyId = familyA
                });
            Assert.True(result.IsSuccess, result.Error?.Description);
            Assert.False(result.Value.ReauthenticationRequired);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            Assert.True(await users.CheckPasswordAsync(user!, "N3wPassw0rd!"));
            Assert.Equal(versionBefore, user!.AuthorizationVersion);
            Assert.Equal(rbacBefore, await ReadRbacAsync(scope.ServiceProvider));
            Assert.Equal(0, await db.RefreshTokens.CountAsync(t =>
                t.UserId == userId && t.FamilyId != familyA && t.RevokedAtUtc == null));
            Assert.True(await db.RefreshTokens.AnyAsync(t =>
                t.FamilyId == familyA && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow));
        }

        await RefreshAsync(sp, first.RefreshToken);
        Assert.Equal(
            AuthenticationErrors.RefreshTokenRevoked.Code,
            (await RefreshRawAsync(sp, second.RefreshToken)).Error!.Code);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(first.AccessToken);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
    }

    [Fact]
    public async Task ChangePassword_WrongPassword_DoesNotChange()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAfterBootstrapAsync(sp, "chg.wrong", "chg.wrong@permixa.test");
        await LoginAsync(sp, "chg.wrong@permixa.test", "Passw0rd!");

        using var scope = sp.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ChangePasswordUseCase>()
            .ExecuteAsync(new ChangePasswordRequest
            {
                UserId = userId,
                CurrentPassword = "nope",
                NewPassword = "N3wPassw0rd!"
            });
        Assert.Equal(AuthenticationErrors.CurrentPasswordInvalid, result.Error);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        Assert.True(await users.CheckPasswordAsync(user!, "Passw0rd!"));
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .RefreshTokens.CountAsync(t => t.UserId == userId && t.RevokedAtUtc == null));
    }

    [Fact]
    public async Task ChangePassword_InvalidPolicy_DoesNotChange()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAfterBootstrapAsync(sp, "chg.pol", "chg.pol@permixa.test");

        using var scope = sp.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<ChangePasswordUseCase>()
            .ExecuteAsync(new ChangePasswordRequest
            {
                UserId = userId,
                CurrentPassword = "Passw0rd!",
                NewPassword = "x"
            });
        Assert.Equal(AuthenticationErrors.InvalidPassword, result.Error);
    }

    [Fact]
    public async Task ChangePassword_MissingOrForeignFamily_RevokesAll()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAfterBootstrapAsync(sp, "chg.fall", "chg.fall@permixa.test");
        await CreateUserAsync(sp, "chg.other", "chg.other@permixa.test");
        var mine = await LoginAsync(sp, "chg.fall@permixa.test", "Passw0rd!");
        var foreign = await LoginAsync(sp, "chg.other@permixa.test", "Passw0rd!");
        var foreignFamily = await FamilyOfAsync(sp, foreign.RefreshToken);

        using (var scope = sp.CreateScope())
        {
            var missing = await scope.ServiceProvider.GetRequiredService<ChangePasswordUseCase>()
                .ExecuteAsync(new ChangePasswordRequest
                {
                    UserId = userId,
                    CurrentPassword = "Passw0rd!",
                    NewPassword = "N3wPassw0rd!"
                });
            Assert.True(missing.Value.ReauthenticationRequired);
        }

        Assert.Equal(
            AuthenticationErrors.RefreshTokenRevoked.Code,
            (await RefreshRawAsync(sp, mine.RefreshToken)).Error!.Code);

        var again = await LoginAsync(sp, "chg.fall@permixa.test", "N3wPassw0rd!");
        using (var scope = sp.CreateScope())
        {
            var foreignResult = await scope.ServiceProvider.GetRequiredService<ChangePasswordUseCase>()
                .ExecuteAsync(new ChangePasswordRequest
                {
                    UserId = userId,
                    CurrentPassword = "N3wPassw0rd!",
                    NewPassword = "N3wPassw0rd2!",
                    CurrentFamilyId = foreignFamily
                });
            Assert.True(foreignResult.Value.ReauthenticationRequired);
        }

        Assert.Equal(
            AuthenticationErrors.RefreshTokenRevoked.Code,
            (await RefreshRawAsync(sp, again.RefreshToken)).Error!.Code);
        await RefreshAsync(sp, foreign.RefreshToken);
    }

    [Fact]
    public async Task ChangePassword_Versus_OtherFamilyRefresh_LeavesOtherFamilyUnusable()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAfterBootstrapAsync(sp, "chg.race", "chg.race@permixa.test");
        var current = await LoginAsync(sp, "chg.race@permixa.test", "Passw0rd!");
        var other = await LoginAsync(sp, "chg.race@permixa.test", "Passw0rd!");
        var currentFamily = await FamilyOfAsync(sp, current.RefreshToken);
        var otherFamily = await FamilyOfAsync(sp, other.RefreshToken);

        await using var refreshScope = sp.CreateAsyncScope();
        await using var changeScope = sp.CreateAsyncScope();
        var refreshTask = refreshScope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
            .ExecuteAsync(new RefreshTokenRequest { RefreshToken = other.RefreshToken });
        var changeTask = changeScope.ServiceProvider.GetRequiredService<ChangePasswordUseCase>()
            .ExecuteAsync(new ChangePasswordRequest
            {
                UserId = userId,
                CurrentPassword = "Passw0rd!",
                NewPassword = "N3wPassw0rd!",
                CurrentFamilyId = currentFamily
            });
        await Task.WhenAll(refreshTask, changeTask);
        var refreshResult = await refreshTask;
        Assert.True((await changeTask).IsSuccess);

        using var assert = sp.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t =>
            t.FamilyId == otherFamily && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow));
        Assert.True(await db.RefreshTokens.AnyAsync(t =>
            t.FamilyId == currentFamily && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow));

        if (refreshResult.IsSuccess)
        {
            var again = await assert.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
                .ExecuteAsync(new RefreshTokenRequest { RefreshToken = refreshResult.Value.RefreshToken });
            Assert.True(again.IsFailure, "A usable continuation must not survive ChangePassword.");
        }
    }

    [Fact]
    public async Task RequestEmailChange_SetsPending_WithoutReplacingCurrentEmail()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAfterBootstrapAsync(sp, "em.req", "em.req@permixa.test");
        await ConfirmCurrentEmailAsync(sp, userId);
        _dispatcher.Deliveries.Clear();
        var rbacBefore = await ReadRbacFromRootAsync(sp);
        var versionBefore = await ReadAuthorizationVersionAsync(sp, userId);

        using (var scope = sp.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<RequestEmailChangeUseCase>()
                .ExecuteAsync(new RequestEmailChangeRequest
                {
                    UserId = userId,
                    CurrentPassword = "Passw0rd!",
                    NewEmail = "em.pending@permixa.test"
                });
            Assert.True(result.IsSuccess, result.Error?.Description);
        }

        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByIdAsync(userId.ToString());
            Assert.Equal("em.req@permixa.test", user!.Email);
            Assert.True(user.EmailConfirmed);
            Assert.Equal("em.pending@permixa.test", user.PendingEmail);
            Assert.Equal(versionBefore, user.AuthorizationVersion);
            Assert.Equal(rbacBefore, await ReadRbacAsync(scope.ServiceProvider));

            var dto = await scope.ServiceProvider.GetRequiredService<IIdentityUserReader>()
                .GetIamUserByIdAsync(userId, DateTime.UtcNow);
            Assert.Equal("em.pending@permixa.test", dto!.PendingEmail);
        }

        Assert.Single(_dispatcher.Deliveries);
        Assert.Equal(VerificationPurpose.EmailChange, _dispatcher.Deliveries[0].Purpose);
        Assert.Equal("em.pending@permixa.test", _dispatcher.Deliveries[0].Destination);
    }

    [Fact]
    public async Task RequestEmailChange_WrongPassword_LeavesUserUnchanged()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAfterBootstrapAsync(sp, "em.wrong", "em.wrong@permixa.test");
        _dispatcher.Deliveries.Clear();

        using var scope = sp.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RequestEmailChangeUseCase>()
            .ExecuteAsync(new RequestEmailChangeRequest
            {
                UserId = userId,
                CurrentPassword = "nope",
                NewEmail = "em.wrong2@permixa.test"
            });
        Assert.Equal(AuthenticationErrors.CurrentPasswordInvalid, result.Error);
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(userId.ToString());
        Assert.Null(user!.PendingEmail);
        Assert.Empty(_dispatcher.Deliveries);
    }

    [Fact]
    public async Task RequestEmailChange_SameOrTakenOrInvalid_Fails()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAfterBootstrapAsync(sp, "em.val", "em.val@permixa.test");
        await CreateUserAsync(sp, "em.taken", "em.taken@permixa.test");

        using var scope = sp.CreateScope();
        var useCase = scope.ServiceProvider.GetRequiredService<RequestEmailChangeUseCase>();
        Assert.Equal(AuthenticationErrors.EmailUnchanged, (await useCase.ExecuteAsync(new RequestEmailChangeRequest
        {
            UserId = userId,
            CurrentPassword = "Passw0rd!",
            NewEmail = "em.val@permixa.test"
        })).Error);
        Assert.Equal(AuthenticationErrors.InvalidEmail, (await useCase.ExecuteAsync(new RequestEmailChangeRequest
        {
            UserId = userId,
            CurrentPassword = "Passw0rd!",
            NewEmail = "not-an-email"
        })).Error);
        Assert.Equal(AuthenticationErrors.EmailAlreadyExists, (await useCase.ExecuteAsync(new RequestEmailChangeRequest
        {
            UserId = userId,
            CurrentPassword = "Passw0rd!",
            NewEmail = "em.taken@permixa.test"
        })).Error);
    }

    [Fact]
    public async Task ReplacePendingEmail_InvalidatesPreviousChallenge()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAfterBootstrapAsync(sp, "em.rep", "em.rep@permixa.test");
        _dispatcher.Deliveries.Clear();

        using var scope = sp.CreateScope();
        var request = scope.ServiceProvider.GetRequiredService<RequestEmailChangeUseCase>();
        var confirm = scope.ServiceProvider.GetRequiredService<ConfirmEmailChangeUseCase>();
        Assert.True((await request.ExecuteAsync(new RequestEmailChangeRequest
        {
            UserId = userId,
            CurrentPassword = "Passw0rd!",
            NewEmail = "em.a@permixa.test"
        })).IsSuccess);
        var challengeA = _dispatcher.Deliveries[^1].ChallengeId;
        var tokenA = _dispatcher.Deliveries[^1].RawVerificationValue;

        Assert.True((await request.ExecuteAsync(new RequestEmailChangeRequest
        {
            UserId = userId,
            CurrentPassword = "Passw0rd!",
            NewEmail = "em.b@permixa.test"
        })).IsSuccess);

        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(userId.ToString());
        Assert.Equal("em.b@permixa.test", user!.PendingEmail);
        Assert.Equal("em.rep@permixa.test", user.Email);

        var stale = await confirm.ExecuteAsync(new ConfirmEmailChangeRequest
        {
            ChallengeId = challengeA,
            Token = tokenA
        });
        Assert.True(stale.IsFailure);
        Assert.Equal("em.rep@permixa.test", (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(userId.ToString()))!.Email);
    }

    [Fact]
    public async Task ConfirmEmailChange_UpdatesIdentityEmail_AndConsumesChallenge()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAfterBootstrapAsync(sp, "em.ok", "em.ok@permixa.test");
        await ConfirmCurrentEmailAsync(sp, userId);
        _dispatcher.Deliveries.Clear();

        using var scope = sp.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<RequestEmailChangeUseCase>()
            .ExecuteAsync(new RequestEmailChangeRequest
            {
                UserId = userId,
                CurrentPassword = "Passw0rd!",
                NewEmail = "em.ok2@permixa.test"
            })).IsSuccess);

        var delivery = _dispatcher.Deliveries[^1];
        var confirmed = await scope.ServiceProvider.GetRequiredService<ConfirmEmailChangeUseCase>()
            .ExecuteAsync(new ConfirmEmailChangeRequest
            {
                ChallengeId = delivery.ChallengeId,
                Token = delivery.RawVerificationValue
            });
        Assert.True(confirmed.IsSuccess, confirmed.Error?.Description);

        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        Assert.Equal("em.ok2@permixa.test", user!.Email);
        Assert.Equal("EM.OK2@PERMIXA.TEST", user.NormalizedEmail);
        Assert.True(user.EmailConfirmed);
        Assert.Null(user.PendingEmail);

        var row = await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .VerificationChallenges.AsNoTracking()
            .SingleAsync(c => c.Id == delivery.ChallengeId);
        Assert.NotNull(row.ConsumedAtUtc);

        var again = await scope.ServiceProvider.GetRequiredService<ConfirmEmailChangeUseCase>()
            .ExecuteAsync(new ConfirmEmailChangeRequest
            {
                ChallengeId = delivery.ChallengeId,
                Token = delivery.RawVerificationValue
            });
        Assert.Equal(VerificationErrors.AlreadyConsumed, again.Error);
    }

    [Fact]
    public async Task ConfirmEmailChange_DuplicateTarget_DoesNotOverwrite()
    {
        await using var sp = BuildProvider();
        var firstId = await CreateUserAfterBootstrapAsync(sp, "em.dup1", "em.dup1@permixa.test");
        var secondId = await CreateUserAsync(sp, "em.dup2", "em.dup2@permixa.test");
        _dispatcher.Deliveries.Clear();

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<RequestEmailChangeUseCase>()
                .ExecuteAsync(new RequestEmailChangeRequest
                {
                    UserId = firstId,
                    CurrentPassword = "Passw0rd!",
                    NewEmail = "em.shared@permixa.test"
                })).IsSuccess);
        }

        var firstDelivery = _dispatcher.Deliveries[^1];
        using (var scope = sp.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(secondId.ToString());
            user!.SetPendingEmail("em.shared@permixa.test");
            await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().UpdateAsync(user);
        }

        using var confirmScope = sp.CreateScope();
        Assert.True((await confirmScope.ServiceProvider.GetRequiredService<ConfirmEmailChangeUseCase>()
            .ExecuteAsync(new ConfirmEmailChangeRequest
            {
                ChallengeId = firstDelivery.ChallengeId,
                Token = firstDelivery.RawVerificationValue
            })).IsSuccess);

        using var assert = sp.CreateScope();
        var second = await assert.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(secondId.ToString());
        Assert.Equal("em.dup2@permixa.test", second!.Email);
        Assert.Equal("em.shared@permixa.test", second.PendingEmail);
    }

    [Fact]
    public async Task AdminRequestEmailChange_SetsPendingOnly()
    {
        await using var sp = BuildProvider();
        var actors = await SeedActorsAsync(sp);
        _dispatcher.Deliveries.Clear();

        using (var scope = sp.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<AdminRequestEmailChangeUseCase>()
                .ExecuteAsync(new AdminRequestEmailChangeCommand(
                    actors.AdminUserId, actors.ClerkUserId, "clerk.new@permixa.test"));
            Assert.True(result.IsSuccess, result.Error?.Description);
        }

        using (var scope = sp.CreateScope())
        {
            var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByIdAsync(actors.ClerkUserId.ToString());
            Assert.Equal("clerk.cred@permixa.test", user!.Email);
            Assert.False(user.EmailConfirmed);
            Assert.Equal("clerk.new@permixa.test", user.PendingEmail);
        }

        Assert.Equal("clerk.new@permixa.test", _dispatcher.Deliveries[^1].Destination);
        Assert.Equal(VerificationPurpose.EmailChange, _dispatcher.Deliveries[^1].Purpose);
    }

    [Fact]
    public async Task AdminRequestEmailChange_DeniedPaths()
    {
        await using var sp = BuildProvider();
        var actors = await SeedActorsAsync(sp);

        using var scope = sp.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<AdminRequestEmailChangeUseCase>();
        Assert.Equal(AuthorizationErrors.CannotManageSelf, (await sut.ExecuteAsync(
            new AdminRequestEmailChangeCommand(actors.AdminUserId, actors.AdminUserId, "x@y.com"))).Error);
        Assert.Equal(AuthorizationErrors.OwnerProtected, (await sut.ExecuteAsync(
            new AdminRequestEmailChangeCommand(actors.AdminUserId, actors.OwnerId, "x@y.com"))).Error);
        Assert.Equal(AuthorizationErrors.HierarchyViolation, (await sut.ExecuteAsync(
            new AdminRequestEmailChangeCommand(actors.AdminUserId, actors.PeerAdminUserId, "x@y.com"))).Error);
        Assert.Equal(AuthorizationErrors.MissingManagePermission, (await sut.ExecuteAsync(
            new AdminRequestEmailChangeCommand(actors.ClerkUserId, actors.NoRoleUserId, "x@y.com"))).Error);
    }

    [Fact]
    public async Task ForcePasswordReset_RevokesSessions_AndCompletesExistingResetFlow()
    {
        await using var sp = BuildProvider();
        var actors = await SeedActorsAsync(sp);
        var clerkLogin = await LoginAsync(sp, "clerk.cred@permixa.test", "Passw0rd!");
        _dispatcher.Deliveries.Clear();

        using (var scope = sp.CreateScope())
        {
            var result = await scope.ServiceProvider.GetRequiredService<ForcePasswordResetUseCase>()
                .ExecuteAsync(new ForcePasswordResetCommand(actors.AdminUserId, actors.ClerkUserId));
            Assert.True(result.IsSuccess, result.Error?.Description);
        }

        Assert.Equal(
            AuthenticationErrors.RefreshTokenRevoked.Code,
            (await RefreshRawAsync(sp, clerkLogin.RefreshToken)).Error!.Code);

        var first = _dispatcher.Deliveries.Single(d => d.Purpose == VerificationPurpose.PasswordReset);
        _dispatcher.Deliveries.Clear();

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<ForcePasswordResetUseCase>()
                .ExecuteAsync(new ForcePasswordResetCommand(actors.AdminUserId, actors.ClerkUserId))).IsSuccess);
        }

        var second = _dispatcher.Deliveries.Single(d => d.Purpose == VerificationPurpose.PasswordReset);
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var prior = await db.VerificationChallenges.AsNoTracking().SingleAsync(c => c.Id == first.ChallengeId);
            Assert.NotNull(prior.InvalidatedAtUtc);
        }

        using (var scope = sp.CreateScope())
        {
            var reset = await scope.ServiceProvider.GetRequiredService<ResetPasswordWithVerificationUseCase>()
                .ExecuteAsync(new ResetPasswordWithVerificationRequest
                {
                    ChallengeId = second.ChallengeId,
                    Token = second.RawVerificationValue,
                    NewPassword = "ClerkN3w!"
                });
            Assert.True(reset.IsSuccess, reset.Error?.Description);
        }

        var login = await LoginAsync(sp, "clerk.cred@permixa.test", "ClerkN3w!");
        Assert.NotNull(login.AccessToken);
        Assert.Equal(
            AuthenticationErrors.RefreshTokenRevoked.Code,
            (await RefreshRawAsync(sp, clerkLogin.RefreshToken)).Error!.Code);

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(clerkLogin.AccessToken);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
    }

    [Fact]
    public async Task ForcePasswordReset_DeniedPaths()
    {
        await using var sp = BuildProvider();
        var actors = await SeedActorsAsync(sp);

        using var scope = sp.CreateScope();
        var sut = scope.ServiceProvider.GetRequiredService<ForcePasswordResetUseCase>();
        Assert.Equal(AuthorizationErrors.CannotManageSelf, (await sut.ExecuteAsync(
            new ForcePasswordResetCommand(actors.AdminUserId, actors.AdminUserId))).Error);
        Assert.Equal(AuthorizationErrors.OwnerProtected, (await sut.ExecuteAsync(
            new ForcePasswordResetCommand(actors.AdminUserId, actors.OwnerId))).Error);
        Assert.Equal(AuthorizationErrors.HierarchyViolation, (await sut.ExecuteAsync(
            new ForcePasswordResetCommand(actors.AdminUserId, actors.PeerAdminUserId))).Error);
        Assert.Equal(AuthorizationErrors.MissingManagePermission, (await sut.ExecuteAsync(
            new ForcePasswordResetCommand(actors.ClerkUserId, actors.NoRoleUserId))).Error);
    }

    [Fact]
    public async Task ForcePasswordReset_Versus_Refresh_LeavesNoActiveContinuation()
    {
        await using var sp = BuildProvider();
        var actors = await SeedActorsAsync(sp);
        var auth = await LoginAsync(sp, "clerk.cred@permixa.test", "Passw0rd!");

        await using var refreshScope = sp.CreateAsyncScope();
        await using var resetScope = sp.CreateAsyncScope();
        var refreshTask = refreshScope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
            .ExecuteAsync(new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
        var resetTask = resetScope.ServiceProvider.GetRequiredService<ForcePasswordResetUseCase>()
            .ExecuteAsync(new ForcePasswordResetCommand(actors.AdminUserId, actors.ClerkUserId));
        await Task.WhenAll(refreshTask, resetTask);
        var refreshResult = await refreshTask;
        Assert.True((await resetTask).IsSuccess);

        using var assert = sp.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        Assert.Equal(0, await db.RefreshTokens.CountAsync(t =>
            t.UserId == actors.ClerkUserId && t.RevokedAtUtc == null && t.ExpiresAtUtc > DateTime.UtcNow));

        if (refreshResult.IsSuccess)
        {
            var again = await assert.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
                .ExecuteAsync(new RefreshTokenRequest { RefreshToken = refreshResult.Value.RefreshToken });
            Assert.True(again.IsFailure, "A usable continuation must not survive ForcePasswordReset.");
        }
    }

    [Fact]
    public async Task AdminCredentialOps_MayTargetLockedOrDisabledUsers()
    {
        await using var sp = BuildProvider();
        var actors = await SeedActorsAsync(sp);

        using (var scope = sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var locked = await users.FindByIdAsync(actors.ClerkUserId.ToString());
            Assert.Equal(IdentityResult.Success, await users.SetLockoutEnabledAsync(locked!, true));
            Assert.Equal(IdentityResult.Success, await users.SetLockoutEndDateAsync(locked!, DateTimeOffset.UtcNow.AddHours(2)));
            locked!.SetDisabled(true);
            Assert.Equal(IdentityResult.Success, await users.UpdateAsync(locked));
        }

        using var assert = sp.CreateScope();
        Assert.True((await assert.ServiceProvider.GetRequiredService<AdminRequestEmailChangeUseCase>()
            .ExecuteAsync(new AdminRequestEmailChangeCommand(
                actors.AdminUserId, actors.ClerkUserId, "locked.new@permixa.test"))).IsSuccess);
        Assert.True((await assert.ServiceProvider.GetRequiredService<ForcePasswordResetUseCase>()
            .ExecuteAsync(new ForcePasswordResetCommand(actors.AdminUserId, actors.ClerkUserId))).IsSuccess);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = _connectionString;
            o.Bootstrap.Enabled = true;
            o.Bootstrap.OwnerEmail = "owner@permixa.test";
            o.Bootstrap.OwnerUserName = "permixa.owner";
            o.Bootstrap.OwnerPassword = "OwnerPass1!";
        });
        services.AddPermixaAuthorization();
        services.AddPermixaAuthentication(o =>
        {
            o.Jwt.Issuer = TestJwtKeys.Issuer;
            o.Jwt.Audience = TestJwtKeys.Audience;
            o.Jwt.PrivateKeyPem = TestJwtKeys.PrivateKeyPem;
            o.Jwt.AccessTokenLifetime = TimeSpan.FromMinutes(15);
            o.Authentication.RefreshTokenLifetime = TimeSpan.FromDays(7);
        });
        services.AddPermixaVerification(o => o.Verification.ResendCooldown = TimeSpan.Zero);
        services.AddSingleton<IVerificationDispatcher>(_dispatcher);
        return services.BuildServiceProvider();
    }

    private static async Task MigrateAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    private static async Task MigrateBootstrapAsync(ServiceProvider sp)
    {
        await MigrateAsync(sp);
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
    }

    private static async Task<Guid> CreateUserAfterBootstrapAsync(ServiceProvider sp, string userName, string email)
    {
        await MigrateBootstrapAsync(sp);
        return await CreateUserAsync(sp, userName, email);
    }

    private static async Task<Guid> CreateUserAsync(ServiceProvider sp, string userName, string email)
    {
        using var scope = sp.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser(userName) { Email = email };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        return user.Id;
    }

    private static async Task ConfirmCurrentEmailAsync(ServiceProvider sp, Guid userId)
    {
        using var scope = sp.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await users.FindByIdAsync(userId.ToString());
        user!.EmailConfirmed = true;
        Assert.Equal(IdentityResult.Success, await users.UpdateAsync(user));
    }

    private static async Task<AuthenticationResult> LoginAsync(ServiceProvider sp, string email, string password)
    {
        using var scope = sp.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<LoginUseCase>()
            .ExecuteAsync(new LoginRequest { EmailOrUserName = email, Password = password });
        Assert.True(result.IsSuccess, result.Error?.Description);
        Assert.True(result.Value.IsAuthenticated);
        return result.Value.Authentication!;
    }

    private static async Task<AuthenticationResult> RefreshAsync(ServiceProvider sp, string refreshToken)
    {
        var result = await RefreshRawAsync(sp, refreshToken);
        Assert.True(result.IsSuccess, result.Error?.Description);
        return result.Value;
    }

    private static async Task<Permixa.Application.Common.Results.Result<AuthenticationResult>> RefreshRawAsync(
        ServiceProvider sp,
        string refreshToken)
    {
        using var scope = sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>()
            .ExecuteAsync(new RefreshTokenRequest { RefreshToken = refreshToken });
    }

    private static async Task<Guid> FamilyOfAsync(ServiceProvider sp, string refreshToken)
    {
        using var scope = sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();
        return (await db.RefreshTokens.SingleAsync(t => t.TokenHash == crypto.HashToken(refreshToken))).FamilyId;
    }

    private static async Task<int> ReadRbacFromRootAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        return await ReadRbacAsync(scope.ServiceProvider);
    }

    private static async Task<int> ReadRbacAsync(IServiceProvider sp) =>
        (await sp.GetRequiredService<ApplicationDbContext>().AuthorizationStates.SingleAsync()).RbacVersion;

    private static async Task<int> ReadAuthorizationVersionAsync(ServiceProvider sp, Guid userId)
    {
        using var scope = sp.CreateScope();
        var user = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByIdAsync(userId.ToString());
        return user!.AuthorizationVersion;
    }

    private static async Task<ActorContext> SeedActorsAsync(ServiceProvider sp)
    {
        await MigrateBootstrapAsync(sp);
        Guid ownerId, adminUserId, peerAdminUserId, clerkUserId, noRoleUserId, adminRoleId;
        using (var scope = sp.CreateScope())
        {
            ownerId = (await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
                .FindByEmailAsync("owner@permixa.test"))!.Id;
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var adminRole = new ApplicationRole("Admin", 10);
            var peerRole = new ApplicationRole("PeerAdmin", 10);
            var clerkRole = new ApplicationRole("Clerk", 20);
            Assert.Equal(IdentityResult.Success, await roles.CreateAsync(adminRole));
            Assert.Equal(IdentityResult.Success, await roles.CreateAsync(peerRole));
            Assert.Equal(IdentityResult.Success, await roles.CreateAsync(clerkRole));
            adminRoleId = adminRole.Id;

            var admin = await CreateNamedAsync(users, "admin.cred", "admin.cred@permixa.test");
            var peer = await CreateNamedAsync(users, "peer.cred", "peer.cred@permixa.test");
            var clerk = await CreateNamedAsync(users, "clerk.cred", "clerk.cred@permixa.test");
            var noRole = await CreateNamedAsync(users, "norole.cred", "norole.cred@permixa.test");
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(admin, "Admin"));
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(peer, "PeerAdmin"));
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(clerk, "Clerk"));
            adminUserId = admin.Id;
            peerAdminUserId = peer.Id;
            clerkUserId = clerk.Id;
            noRoleUserId = noRole.Id;

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            foreach (var name in new[] { IamPermissions.Users.ChangeEmail, IamPermissions.Users.ForcePasswordReset })
            {
                var permissionId = (await db.Permissions.SingleAsync(p => p.Name == name)).Id;
                Assert.True((await scope.ServiceProvider.GetRequiredService<AssignPermissionToRoleUseCase>()
                    .ExecuteAsync(new AssignPermissionToRoleCommand(ownerId, adminRoleId, permissionId))).IsSuccess);
            }
        }

        return new ActorContext(ownerId, adminUserId, peerAdminUserId, clerkUserId, noRoleUserId);
    }

    private static async Task<ApplicationUser> CreateNamedAsync(
        UserManager<ApplicationUser> users, string userName, string email)
    {
        var user = new ApplicationUser(userName) { Email = email };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        return user;
    }

    private sealed record ActorContext(
        Guid OwnerId,
        Guid AdminUserId,
        Guid PeerAdminUserId,
        Guid ClerkUserId,
        Guid NoRoleUserId);
}
