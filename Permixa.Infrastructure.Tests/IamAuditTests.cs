using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Audit.Get;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.ChangePassword;
using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Logout;
using Permixa.Application.Authentication.Mfa.BeginSetup;
using Permixa.Application.Authentication.Mfa.Enable;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Permissions.Create;
using Permixa.Application.Authorization.RolePermissions.Assign;
using Permixa.Application.Authorization.Roles.ChangePosition;
using Permixa.Application.Authorization.Roles.Create;
using Permixa.Application.Authorization.Roles.Delete;
using Permixa.Application.Authorization.Roles.Rename;
using Permixa.Application.Authorization.Sessions.Revoke;
using Permixa.Application.Authorization.UserPermissionOverrides.Set;
using Permixa.Application.Authorization.Users.Disable;
using Permixa.Application.Authorization.Users.ForcePasswordReset;
using Permixa.Application.Authorization.Users.Lock;
using Permixa.Application.Common.Paging;
using Permixa.Application.Verification.Abstractions;
using Permixa.Application.Verification.EmailChange;
using Permixa.Domain.Audit;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.Infrastructure.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class IamAuditTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;
    private CapturingVerificationDispatcher _dispatcher = null!;

    public IamAuditTests(SqlServerContainerFixture sqlServer)
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
    public async Task Migration_AddsIamAuditLogs_WithoutForeignKeys()
    {
        await using var sp = BuildProvider();
        await MigrateAsync(sp);

        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();

        await using (var columns = connection.CreateCommand())
        {
            columns.CommandText = """
                SELECT c.name, t.name AS type_name, c.max_length, c.is_nullable
                FROM sys.columns c
                INNER JOIN sys.types t ON c.user_type_id = t.user_type_id
                INNER JOIN sys.tables tab ON c.object_id = tab.object_id
                WHERE tab.name = N'IamAuditLogs'
                ORDER BY c.column_id
                """;
            var rows = new List<(string Name, string Type, short MaxLength, bool Nullable)>();
            await using var reader = await columns.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                rows.Add((reader.GetString(0), reader.GetString(1), reader.GetInt16(2), reader.GetBoolean(3)));
            }

            AssertColumn(rows, "Id", "uniqueidentifier", 16, false);
            AssertColumn(rows, "OccurredAtUtc", "datetime2", 8, false);
            AssertColumn(rows, "EventType", "nvarchar", 256, false);
            AssertColumn(rows, "Outcome", "nvarchar", 32, false);
            AssertColumn(rows, "ActorUserId", "uniqueidentifier", 16, true);
            AssertColumn(rows, "TargetUserId", "uniqueidentifier", 16, true);
            AssertColumn(rows, "TargetRoleId", "uniqueidentifier", 16, true);
            AssertColumn(rows, "TargetPermissionId", "uniqueidentifier", 16, true);
            AssertColumn(rows, "CorrelationId", "nvarchar", 128, true);
            AssertColumn(rows, "MetadataJson", "nvarchar", -1, true);
        }

        await using (var indexes = connection.CreateCommand())
        {
            indexes.CommandText = """
                SELECT i.name
                FROM sys.indexes i
                INNER JOIN sys.tables t ON i.object_id = t.object_id
                WHERE t.name = N'IamAuditLogs' AND i.name IS NOT NULL
                ORDER BY i.name
                """;
            var names = new List<string>();
            await using var reader = await indexes.ExecuteReaderAsync();
            while (await reader.ReadAsync())
                names.Add(reader.GetString(0));

            Assert.Contains("IX_IamAuditLogs_OccurredAtUtc_Id", names);
            Assert.Contains("IX_IamAuditLogs_ActorUserId", names);
            Assert.Contains("IX_IamAuditLogs_TargetUserId", names);
            Assert.Contains("IX_IamAuditLogs_EventType", names);
        }

        await using var foreignKeys = connection.CreateCommand();
        foreignKeys.CommandText = """
            SELECT fk.name
            FROM sys.foreign_keys fk
            INNER JOIN sys.tables t ON fk.parent_object_id = t.object_id
            WHERE t.name = N'IamAuditLogs'
            """;
        await using var fkReader = await foreignKeys.ExecuteReaderAsync();
        Assert.False(await fkReader.ReadAsync());
    }

    [Fact]
    public async Task Write_PersistsSingleRow_WithSafeMetadata()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);

        Guid permissionId;
        using (var scope = sp.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreatePermissionUseCase>()
                .ExecuteAsync(new CreatePermissionCommand(ownerId, "Audit.Write.Test", null));
            Assert.True(created.IsSuccess, created.Error?.Description);
            permissionId = created.Value.Id;
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.IamAuditLogs.CountAsync());
            var row = await db.IamAuditLogs.SingleAsync();
            Assert.Equal(IamAuditEvents.Permissions.Created, row.EventType);
            Assert.Equal("Success", row.Outcome);
            Assert.Equal(ownerId, row.ActorUserId);
            Assert.Equal(permissionId, row.TargetPermissionId);
            Assert.Null(row.MetadataJson);
        }
    }

    [Fact]
    public async Task DisableUser_AuditFailure_RollsBackMutation()
    {
        await using var sp = BuildProvider(throwingAudit: true);
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        var targetId = await CreateUserAsync(sp, "audit.disable", "audit.disable@permixa.test");

        using (var scope = sp.CreateScope())
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<DisableUserUseCase>()
                    .ExecuteAsync(new DisableUserCommand(ownerId, targetId)));
            Assert.Contains("Forced audit failure", ex.Message, StringComparison.Ordinal);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Id == targetId);
            Assert.False(user.IsDisabled);
            Assert.Equal(0, await db.IamAuditLogs.CountAsync());
        }
    }

    [Fact]
    public async Task CreatePermission_AuditFailure_RollsBack()
    {
        await using var sp = BuildProvider(throwingAudit: true);
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        const string name = "Audit.Failed.Permission";

        using (var scope = sp.CreateScope())
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<CreatePermissionUseCase>()
                    .ExecuteAsync(new CreatePermissionCommand(ownerId, name, null)));
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(0, await db.Permissions.CountAsync(p => p.Name == name));
            Assert.Equal(0, await db.IamAuditLogs.CountAsync());
        }
    }

    [Fact]
    public async Task RoleLifecycle_OneEventEach()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);

        Guid adminId;
        Guid employeeId;
        Guid lifeRoleId;
        using (var scope = sp.CreateScope())
        {
            adminId = await SeedRoleAsync(scope.ServiceProvider, "Admin", 10);
            employeeId = await SeedRoleAsync(scope.ServiceProvider, "Employee", 20);
        }

        using (var scope = sp.CreateScope())
        {
            var created = await scope.ServiceProvider.GetRequiredService<CreateRoleUseCase>()
                .ExecuteAsync(new CreateRoleCommand(ownerId, "LifeRole", adminId, RolePlacement.Below));
            Assert.True(created.IsSuccess, created.Error?.Description);
            lifeRoleId = created.Value.Id;
        }

        using (var scope = sp.CreateScope())
        {
            var renamed = await scope.ServiceProvider.GetRequiredService<RenameRoleUseCase>()
                .ExecuteAsync(new RenameRoleCommand(ownerId, lifeRoleId, "LifeRenamed"));
            Assert.True(renamed.IsSuccess, renamed.Error?.Description);
        }

        using (var scope = sp.CreateScope())
        {
            var moved = await scope.ServiceProvider.GetRequiredService<ChangeRolePositionUseCase>()
                .ExecuteAsync(new ChangeRolePositionCommand(ownerId, lifeRoleId, employeeId, RolePlacement.Below));
            Assert.True(moved.IsSuccess, moved.Error?.Description);
        }

        using (var scope = sp.CreateScope())
        {
            var deleted = await scope.ServiceProvider.GetRequiredService<DeleteRoleUseCase>()
                .ExecuteAsync(new DeleteRoleCommand(ownerId, lifeRoleId));
            Assert.True(deleted.IsSuccess, deleted.Error?.Description);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var events = await db.IamAuditLogs
                .Where(l => l.TargetRoleId == lifeRoleId)
                .Select(l => l.EventType)
                .ToListAsync();

            Assert.Equal(1, events.Count(e => e == IamAuditEvents.Roles.Created));
            Assert.Equal(1, events.Count(e => e == IamAuditEvents.Roles.Renamed));
            Assert.Equal(1, events.Count(e => e == IamAuditEvents.Roles.Repositioned));
            Assert.Equal(1, events.Count(e => e == IamAuditEvents.Roles.Deleted));
            Assert.Equal(1, await db.IamAuditLogs.CountAsync(l => l.EventType == IamAuditEvents.Roles.Repositioned));
        }
    }

    [Fact]
    public async Task UserAdmin_LockUnlockDisableEnable_Audit()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        var targetId = await CreateUserAsync(sp, "audit.target", "audit.target@permixa.test");

        var lockedUntil = DateTime.UtcNow.AddHours(1);
        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<LockUserUseCase>()
                .ExecuteAsync(new LockUserCommand(ownerId, targetId, lockedUntil))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<Permixa.Application.Authorization.Users.Lock.UnlockUserUseCase>()
                .ExecuteAsync(new Permixa.Application.Authorization.Users.Lock.UnlockUserCommand(ownerId, targetId))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<DisableUserUseCase>()
                .ExecuteAsync(new DisableUserCommand(ownerId, targetId))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<EnableUserUseCase>()
                .ExecuteAsync(new EnableUserCommand(ownerId, targetId))).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await db.IamAuditLogs
                .Where(l => l.TargetUserId == targetId)
                .OrderBy(l => l.OccurredAtUtc)
                .ToListAsync();

            Assert.Equal(4, rows.Count);
            Assert.All(rows, r =>
            {
                Assert.Equal(ownerId, r.ActorUserId);
                Assert.Equal(targetId, r.TargetUserId);
            });
            Assert.Equal(IamAuditEvents.Users.Locked, rows[0].EventType);
            Assert.Equal(IamAuditEvents.Users.Unlocked, rows[1].EventType);
            Assert.Equal(IamAuditEvents.Users.Disabled, rows[2].EventType);
            Assert.Equal(IamAuditEvents.Users.Enabled, rows[3].EventType);
        }
    }

    [Fact]
    public async Task AssignPermissionAndOverride_Audit_DoesNotExtraBumpVersions()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);

        Guid clerkRoleId;
        Guid clerkId;
        Guid permissionId;
        int rbacBefore;
        int userVersionBefore;

        using (var scope = sp.CreateScope())
        {
            var roles = scope.ServiceProvider.GetRequiredService<RoleManager<ApplicationRole>>();
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var clerkRole = new ApplicationRole("Clerk", 50);
            Assert.Equal(IdentityResult.Success, await roles.CreateAsync(clerkRole));
            clerkRoleId = clerkRole.Id;

            var clerk = new ApplicationUser("audit.clerk") { Email = "audit.clerk@permixa.test" };
            Assert.Equal(IdentityResult.Success, await users.CreateAsync(clerk, "Passw0rd!"));
            Assert.Equal(IdentityResult.Success, await users.AddToRoleAsync(clerk, "Clerk"));
            clerkId = clerk.Id;

            var created = await scope.ServiceProvider.GetRequiredService<CreatePermissionUseCase>()
                .ExecuteAsync(new CreatePermissionCommand(ownerId, "Audit.Version.Test", null));
            Assert.True(created.IsSuccess);
            permissionId = created.Value.Id;

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            rbacBefore = (await db.AuthorizationStates.SingleAsync()).RbacVersion;
            userVersionBefore = clerk.AuthorizationVersion;
        }

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<AssignPermissionToRoleUseCase>()
                .ExecuteAsync(new AssignPermissionToRoleCommand(ownerId, clerkRoleId, permissionId))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<SetUserPermissionOverrideUseCase>()
                .ExecuteAsync(new SetUserPermissionOverrideCommand(
                    ownerId, clerkId, permissionId, PermissionEffect.Deny))).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(rbacBefore + 1, (await db.AuthorizationStates.SingleAsync()).RbacVersion);
            Assert.Equal(userVersionBefore + 1, (await db.Users.SingleAsync(u => u.Id == clerkId)).AuthorizationVersion);

            Assert.Equal(1, await db.IamAuditLogs.CountAsync(l => l.EventType == IamAuditEvents.RolePermissions.Assigned));
            Assert.Equal(1, await db.IamAuditLogs.CountAsync(l => l.EventType == IamAuditEvents.UserPermissionOverrides.Set));
        }
    }

    [Fact]
    public async Task SessionRevokeAndLogout_OneEventEach()
    {
        await using var sp = BuildProvider();
        var userId = await CreateUserAfterBootstrapAsync(sp, "audit.sess", "audit.sess@permixa.test");
        var loginA = await LoginAsync(sp, "audit.sess@permixa.test", "Passw0rd!");
        var loginB = await LoginAsync(sp, "audit.sess@permixa.test", "Passw0rd!");
        var loginC = await LoginAsync(sp, "audit.sess@permixa.test", "Passw0rd!");

        Guid familyA;
        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();
            familyA = (await db.RefreshTokens.SingleAsync(t =>
                t.TokenHash == crypto.HashToken(loginA.RefreshToken))).FamilyId;
        }

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<RevokeMySessionUseCase>()
                .ExecuteAsync(new RevokeMySessionCommand(userId, familyA))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<RevokeAllMySessionsUseCase>()
                .ExecuteAsync(new RevokeAllMySessionsCommand(userId))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<LogoutUseCase>()
                .ExecuteAsync(new RevokeRefreshTokenRequest { RefreshToken = loginC.RefreshToken })).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var sessionEvents = await db.IamAuditLogs
                .Where(l => l.EventType == IamAuditEvents.Sessions.Revoked
                    || l.EventType == IamAuditEvents.Sessions.RevokedAll
                    || l.EventType == IamAuditEvents.Sessions.Logout)
                .ToListAsync();

            Assert.Equal(1, sessionEvents.Count(e => e.EventType == IamAuditEvents.Sessions.Revoked));
            Assert.Equal(1, sessionEvents.Count(e => e.EventType == IamAuditEvents.Sessions.RevokedAll));
            Assert.Equal(1, sessionEvents.Count(e => e.EventType == IamAuditEvents.Sessions.Logout));

            var sensitive = new[] { loginA.RefreshToken, loginB.RefreshToken, loginC.RefreshToken };
            foreach (var row in sessionEvents)
            {
                AssertNoRefreshSecretsInMetadata(row.MetadataJson, sensitive);
            }
        }
    }

    [Fact]
    public async Task CredentialsAndMfa_NeverPersistSecrets()
    {
        await using var sp = BuildCredentialsProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        var userId = await CreateUserAsync(sp, "audit.secrets", "audit.secrets@permixa.test");
        await ConfirmEmailAsync(sp, userId);

        var login = await LoginAsync(sp, "audit.secrets@permixa.test", "Passw0rd!");
        const string newPassword = "N3wPassw0rd!";
        string totp = null!;
        IReadOnlyList<string> recoveryCodes = Array.Empty<string>();

        using (var scope = sp.CreateScope())
        {
            var changed = await scope.ServiceProvider.GetRequiredService<ChangePasswordUseCase>()
                .ExecuteAsync(new ChangePasswordRequest
                {
                    UserId = userId,
                    CurrentPassword = "Passw0rd!",
                    NewPassword = newPassword,
                    CurrentFamilyId = null
                });
            Assert.True(changed.IsSuccess, changed.Error?.Description);

            Assert.True((await scope.ServiceProvider.GetRequiredService<RequestEmailChangeUseCase>()
                .ExecuteAsync(new RequestEmailChangeRequest
                {
                    UserId = userId,
                    CurrentPassword = newPassword,
                    NewEmail = "audit.secrets.new@permixa.test"
                })).IsSuccess);

            await scope.ServiceProvider.GetRequiredService<BeginAuthenticatorSetupUseCase>()
                .ExecuteAsync(new BeginAuthenticatorSetupCommand(userId));
            totp = await IdentityTotpTestHelper.GenerateAsync(
                scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(),
                userId);
            var enabled = await scope.ServiceProvider.GetRequiredService<EnableAuthenticatorMfaUseCase>()
                .ExecuteAsync(new EnableAuthenticatorMfaCommand(userId, totp));
            Assert.True(enabled.IsSuccess, enabled.Error?.Description);
            recoveryCodes = enabled.Value.RecoveryCodes;
            Assert.NotEmpty(recoveryCodes);

            Assert.True((await scope.ServiceProvider.GetRequiredService<ForcePasswordResetUseCase>()
                .ExecuteAsync(new ForcePasswordResetCommand(ownerId, userId))).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var rows = await db.IamAuditLogs.AsNoTracking().ToListAsync();
            Assert.NotEmpty(rows);

            var forbiddenValues = new List<string>
            {
                "Passw0rd!",
                newPassword,
                login.RefreshToken,
                totp
            };
            forbiddenValues.AddRange(recoveryCodes);

            foreach (var row in rows)
            {
                AssertNoSecretLeak(row.EventType, row.MetadataJson, forbiddenValues);
            }
        }
    }

    [Fact]
    public async Task GetIamAuditLogs_RequiresPermission_AndPagesNewestFirst()
    {
        await using var sp = BuildProvider();
        var ownerId = await MigrateBootstrapAndGetOwnerAsync(sp);
        var clerkId = await CreateUserAsync(sp, "audit.reader", "audit.reader@permixa.test");
        var targetId = await CreateUserAsync(sp, "audit.paged", "audit.paged@permixa.test");

        using (var scope = sp.CreateScope())
        {
            Assert.True((await scope.ServiceProvider.GetRequiredService<LockUserUseCase>()
                .ExecuteAsync(new LockUserCommand(ownerId, targetId, DateTime.UtcNow.AddHours(1)))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<Permixa.Application.Authorization.Users.Lock.UnlockUserUseCase>()
                .ExecuteAsync(new Permixa.Application.Authorization.Users.Lock.UnlockUserCommand(ownerId, targetId))).IsSuccess);
            Assert.True((await scope.ServiceProvider.GetRequiredService<DisableUserUseCase>()
                .ExecuteAsync(new DisableUserCommand(ownerId, targetId))).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var denied = await scope.ServiceProvider.GetRequiredService<GetIamAuditLogsUseCase>()
                .ExecuteAsync(new GetIamAuditLogsQuery(clerkId, new PageRequest(1, 20)));
            Assert.Equal(AuthorizationErrors.MissingManagePermission, denied.Error);
        }

        using (var scope = sp.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var auditPermissionId = (await db.Permissions.SingleAsync(p => p.Name == IamPermissions.Audit.Read)).Id;
            Assert.True((await scope.ServiceProvider.GetRequiredService<SetUserPermissionOverrideUseCase>()
                .ExecuteAsync(new SetUserPermissionOverrideCommand(
                    ownerId, clerkId, auditPermissionId, PermissionEffect.Allow))).IsSuccess);
        }

        using (var scope = sp.CreateScope())
        {
            var filtered = await scope.ServiceProvider.GetRequiredService<GetIamAuditLogsUseCase>()
                .ExecuteAsync(new GetIamAuditLogsQuery(
                    clerkId,
                    new PageRequest(1, 10),
                    FilterTargetUserId: targetId,
                    EventType: IamAuditEvents.Users.Disabled,
                    Outcome: IamAuditOutcome.Success));
            Assert.True(filtered.IsSuccess, filtered.Error?.Description);
            Assert.Single(filtered.Value.Items);
            Assert.Equal(IamAuditEvents.Users.Disabled, filtered.Value.Items[0].EventType);
            Assert.Equal(targetId, filtered.Value.Items[0].TargetUserId);
        }

        using (var scope = sp.CreateScope())
        {
            var page = await scope.ServiceProvider.GetRequiredService<GetIamAuditLogsUseCase>()
                .ExecuteAsync(new GetIamAuditLogsQuery(ownerId, new PageRequest(1, 50)));
            Assert.True(page.IsSuccess, page.Error?.Description);
            Assert.True(page.Value.Items.Count >= 3);

            // OccurredAtUtc DESC is required. Id DESC uses SQL uniqueidentifier order
            // (not .NET Guid.CompareTo), so only assert time is non-increasing here.
            for (var i = 1; i < page.Value.Items.Count; i++)
            {
                Assert.True(
                    page.Value.Items[i - 1].OccurredAtUtc >= page.Value.Items[i].OccurredAtUtc);
            }
        }
    }

    [Fact]
    public void CustomSink_RegisteredBeforeInfrastructure_IsNotReplaced()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped<IIamAuditSink, FakeIamAuditSink>();
        services.AddPermixaInfrastructure(o => o.ConnectionString = _connectionString);
        services.AddPermixaAuthorization();

        using var scope = services.BuildServiceProvider().CreateScope();
        Assert.IsType<FakeIamAuditSink>(scope.ServiceProvider.GetRequiredService<IIamAuditSink>());
    }

    [Fact]
    public async Task Bootstrap_SeedsIamAuditRead_ForOwner()
    {
        await using var sp = BuildProvider();
        await MigrateAsync(sp);

        using (var scope = sp.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
            await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
        }

        using var assert = sp.CreateScope();
        var db = assert.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = assert.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var effective = assert.ServiceProvider.GetRequiredService<IEffectivePermissionService>();

        Assert.Equal(1, await db.Permissions.CountAsync(p => p.Name == IamPermissions.Audit.Read));
        Assert.Equal(IamPermissions.All.Count, await db.Permissions.CountAsync());
        Assert.Equal(IamPermissions.All.Count, await db.RolePermissions.CountAsync());

        var owner = await users.FindByEmailAsync("owner@permixa.test");
        Assert.NotNull(owner);
        var snapshot = await effective.GetAuthorizationSnapshotAsync(owner.Id);
        Assert.True(snapshot.IsSuccess);
        Assert.True(snapshot.Value.HasPermission(IamPermissions.Audit.Read));
    }

    private ServiceProvider BuildProvider(bool throwingAudit = false)
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

        if (throwingAudit)
            ReplaceWithThrowingAuditSink(services);

        return services.BuildServiceProvider();
    }

    private ServiceProvider BuildCredentialsProvider()
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

    private static void ReplaceWithThrowingAuditSink(IServiceCollection services)
    {
        foreach (var descriptor in services.Where(d => d.ServiceType == typeof(IIamAuditSink)).ToList())
            services.Remove(descriptor);
        services.AddScoped<IIamAuditSink, ThrowingIamAuditSink>();
    }

    private static async Task MigrateAsync(ServiceProvider sp)
    {
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    private static async Task<Guid> MigrateBootstrapAndGetOwnerAsync(ServiceProvider sp)
    {
        await MigrateAsync(sp);
        using var scope = sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IPermixaBootstrapper>().BootstrapAsync();
        var owner = await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>()
            .FindByEmailAsync("owner@permixa.test");
        return owner!.Id;
    }

    private static async Task<Guid> CreateUserAfterBootstrapAsync(ServiceProvider sp, string userName, string email)
    {
        await MigrateBootstrapAndGetOwnerAsync(sp);
        return await CreateUserAsync(sp, userName, email);
    }

    private static async Task<Guid> CreateUserAsync(ServiceProvider sp, string userName, string email)
    {
        using var scope = sp.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = new ApplicationUser(userName) { Email = email, EmailConfirmed = true };
        Assert.Equal(IdentityResult.Success, await users.CreateAsync(user, "Passw0rd!"));
        return user.Id;
    }

    private static async Task ConfirmEmailAsync(ServiceProvider sp, Guid userId)
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

    private static async Task<Guid> SeedRoleAsync(IServiceProvider sp, string name, int level)
    {
        var roles = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var role = new ApplicationRole(name, level);
        Assert.Equal(IdentityResult.Success, await roles.CreateAsync(role));
        return role.Id;
    }

    private static void AssertColumn(
        IReadOnlyList<(string Name, string Type, short MaxLength, bool Nullable)> rows,
        string name,
        string type,
        short maxLength,
        bool nullable)
    {
        var row = Assert.Single(rows, r => r.Name == name);
        Assert.Equal(type, row.Type, StringComparer.OrdinalIgnoreCase);
        Assert.Equal(maxLength, row.MaxLength);
        Assert.Equal(nullable, row.Nullable);
    }

    private static void AssertNoRefreshSecretsInMetadata(string? metadataJson, IEnumerable<string> rawTokens)
    {
        if (string.IsNullOrEmpty(metadataJson))
            return;

        Assert.DoesNotContain("RefreshToken", metadataJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TokenHash", metadataJson, StringComparison.OrdinalIgnoreCase);
        foreach (var token in rawTokens)
        {
            Assert.DoesNotContain(token, metadataJson, StringComparison.Ordinal);
        }
    }

    private static void AssertNoSecretLeak(string eventType, string? metadataJson, IEnumerable<string> forbiddenValues)
    {
        foreach (var value in forbiddenValues.Where(v => !string.IsNullOrWhiteSpace(v)))
        {
            Assert.DoesNotContain(value, metadataJson ?? string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(value, eventType, StringComparison.Ordinal);
        }

        if (string.IsNullOrEmpty(metadataJson))
            return;

        foreach (var token in new[] { "RefreshToken", "TokenHash", "MfaProof", "ProofHash", "RecoveryCode", "AuthenticatorKey" })
        {
            Assert.DoesNotContain(token, metadataJson, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class ThrowingIamAuditSink : IIamAuditSink
    {
        public Task WriteAsync(IamAuditEvent e, CancellationToken ct = default) =>
            throw new InvalidOperationException("Forced audit failure.");
    }

    private sealed class FakeIamAuditSink : IIamAuditSink
    {
        public Task WriteAsync(IamAuditEvent e, CancellationToken ct = default) => Task.CompletedTask;
    }
}
