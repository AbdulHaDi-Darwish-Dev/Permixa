using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Register;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.EffectivePermissions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Permissions.Create;
using Permixa.Application.Authorization.Permissions.Get;
using Permixa.Application.Authorization.Permissions.Update;
using Permixa.Application.Authorization.RolePermissions.Assign;
using Permixa.Application.Authorization.RolePermissions.Get;
using Permixa.Application.Authorization.RolePermissions.Remove;
using Permixa.Application.Authorization.Roles.ChangePosition;
using Permixa.Application.Authorization.Roles.Create;
using Permixa.Application.Authorization.Roles.Delete;
using Permixa.Application.Authorization.Roles.Get;
using Permixa.Application.Authorization.Roles.Rename;
using Permixa.Application.Authorization.UserPermissionOverrides.Get;
using Permixa.Application.Authorization.UserPermissionOverrides.Remove;
using Permixa.Application.Authorization.UserPermissionOverrides.Set;
using Permixa.Application.Authorization.UserRoles.Assign;
using Permixa.Application.Authorization.UserRoles.Get;
using Permixa.Application.Authorization.UserRoles.Remove;
using Permixa.Application.Authorization.Users.Create;
using Permixa.Application.Authorization.Users.Disable;
using Permixa.Application.Authorization.Users.Get;
using Permixa.Application.Authorization.Sessions.Get;
using Permixa.Application.Authorization.Sessions.Revoke;
using Permixa.Application.Authorization.Users.Lock;
using Permixa.Application.Common;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.Infrastructure.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class PermissionRepositoryTests : InfrastructureTestBase
{
    public PermissionRepositoryTests(SqlServerContainerFixture sqlServer)
        : base(sqlServer)
    {
    }

    [Fact]
    public async Task Permission_Name_IsUnique()
    {
        using var scope = Fixture.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPermissionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        await repo.AddAsync(Permission.Create("Users.Read"));
        await uow.SaveChangesAsync();

        await repo.AddAsync(Permission.Create("Users.Read"));
        await Assert.ThrowsAsync<DbUpdateException>(() => uow.SaveChangesAsync());
    }

    [Fact]
    public async Task GetByIdsAsync_QueryCount_DoesNotGrowWithIdCount()
    {
        using var scope = Fixture.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<IPermissionRepository>();
        var uow = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var permissions = Enumerable.Range(1, 40)
            .Select(i => Permission.Create($"Orders.Action{i}"))
            .ToArray();

        foreach (var permission in permissions)
            await repo.AddAsync(permission);

        await uow.SaveChangesAsync();

        Fixture.Interceptor.Reset();
        var one = await repo.GetByIdsAsync([permissions[0].Id]);
        var countForOne = Fixture.Interceptor.Count;

        Fixture.Interceptor.Reset();
        var many = await repo.GetByIdsAsync(permissions.Select(p => p.Id).ToArray());
        var countForMany = Fixture.Interceptor.Count;

        Assert.Single(one);
        Assert.Equal(40, many.Count);
        Assert.Equal(countForOne, countForMany);
        Assert.True(countForMany <= 2, $"Expected bounded queries, got {countForMany}");
    }

    [Fact]
    public async Task GetByRoleIdsAsync_IsSetBased_ForMultipleRoles()
    {
        using var scope = Fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var permissionRepo = sp.GetRequiredService<IPermissionRepository>();
        var rolePermissionRepo = sp.GetRequiredService<IRolePermissionRepository>();
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();

        var roleA = new ApplicationRole("RoleA", 20);
        var roleB = new ApplicationRole("RoleB", 30);
        await roleManager.CreateAsync(roleA);
        await roleManager.CreateAsync(roleB);

        var p1 = Permission.Create("Orders.Read");
        var p2 = Permission.Create("Orders.Write");
        var p3 = Permission.Create("Orders.Delete");
        await permissionRepo.AddAsync(p1);
        await permissionRepo.AddAsync(p2);
        await permissionRepo.AddAsync(p3);
        await rolePermissionRepo.AddAsync(RolePermission.Create(roleA.Id, p1.Id));
        await rolePermissionRepo.AddAsync(RolePermission.Create(roleA.Id, p2.Id));
        await rolePermissionRepo.AddAsync(RolePermission.Create(roleB.Id, p3.Id));
        await uow.SaveChangesAsync();

        Fixture.Interceptor.Reset();
        var result = await permissionRepo.GetByRoleIdsAsync([roleA.Id, roleB.Id]);
        var queryCount = Fixture.Interceptor.Count;

        Assert.Equal(3, result.Count);
        Assert.True(queryCount <= 2, $"Expected set-based query, got {queryCount}");
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class RoleHierarchyReaderTests : InfrastructureTestBase
{
    public RoleHierarchyReaderTests(SqlServerContainerFixture sqlServer)
        : base(sqlServer)
    {
    }

    [Fact]
    public async Task RoleLevel20_Outranks_RoleLevel30_ForEffectiveMin()
    {
        using var scope = Fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var hierarchy = sp.GetRequiredService<IRoleHierarchyReader>();

        var admin = new ApplicationRole("Admin", 20);
        var manager = new ApplicationRole("Manager", 30);
        await roleManager.CreateAsync(admin);
        await roleManager.CreateAsync(manager);

        var user = new ApplicationUser("combo") { Email = "combo@test.local" };
        await userManager.CreateAsync(user, "Passw0rd!");
        await userManager.AddToRoleAsync(user, "Admin");
        await userManager.AddToRoleAsync(user, "Manager");

        var effective = await hierarchy.GetEffectiveUserLevelAsync(user.Id);
        Assert.Equal(20, effective);
    }

    [Fact]
    public async Task UserWithRoles30And50_EffectiveLevelIs30()
    {
        using var scope = Fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var hierarchy = sp.GetRequiredService<IRoleHierarchyReader>();

        await roleManager.CreateAsync(new ApplicationRole("Manager", 30));
        await roleManager.CreateAsync(new ApplicationRole("Accountant", 50));

        var user = new ApplicationUser("levels") { Email = "levels@test.local" };
        await userManager.CreateAsync(user, "Passw0rd!");
        await userManager.AddToRoleAsync(user, "Manager");
        await userManager.AddToRoleAsync(user, "Accountant");

        Assert.Equal(30, await hierarchy.GetEffectiveUserLevelAsync(user.Id));
    }

    [Fact]
    public async Task UserWithNoRoles_ReturnsNull_NotZero()
    {
        using var scope = Fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var hierarchy = sp.GetRequiredService<IRoleHierarchyReader>();

        var user = new ApplicationUser("norole") { Email = "norole@test.local" };
        await userManager.CreateAsync(user, "Passw0rd!");

        var effective = await hierarchy.GetEffectiveUserLevelAsync(user.Id);
        Assert.Null(effective);
    }

    [Fact]
    public void ApplicationRole_RejectsLevelZero()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ApplicationRole("Broken", 0));
    }

    [Fact]
    public async Task GetEffectiveUserLevelAsync_IsSetBased()
    {
        using var scope = Fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var hierarchy = sp.GetRequiredService<IRoleHierarchyReader>();
        await SeedUserWithRoleAsync(sp, "hieruser", "HierRole", 40);

        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var userManager = sp.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.FindByNameAsync("hieruser");
        Assert.NotNull(user);

        for (var i = 0; i < 8; i++)
        {
            var r = new ApplicationRole($"Extra{i}", 50 + i);
            await roleManager.CreateAsync(r);
            await userManager.AddToRoleAsync(user!, r.Name!);
        }

        Fixture.Interceptor.Reset();
        var level = await hierarchy.GetEffectiveUserLevelAsync(user!.Id);
        var queries = Fixture.Interceptor.Count;

        Assert.Equal(40, level);
        Assert.True(queries <= 2, $"Expected set-based hierarchy query, got {queries}");
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class AuthorizationVersionAndStateTests : InfrastructureTestBase
{
    private readonly SqlServerContainerFixture _sqlServer;

    public AuthorizationVersionAndStateTests(SqlServerContainerFixture sqlServer)
        : base(sqlServer)
    {
        _sqlServer = sqlServer;
    }

    [Fact]
    public async Task AuthorizationState_Bootstrap_IsIdempotent()
    {
        using var scope = Fixture.CreateScope();
        var bootstrapper = scope.ServiceProvider.GetRequiredService<IAuthorizationStateBootstrapper>();
        var states = scope.ServiceProvider.GetRequiredService<IAuthorizationStateRepository>();

        await bootstrapper.EnsureCreatedAsync();
        await bootstrapper.EnsureCreatedAsync();

        var state = await states.GetAsync();
        Assert.NotNull(state);
        Assert.Equal(AuthorizationState.GlobalId, state!.Id);
        Assert.Equal(1, state.RbacVersion);
    }

    [Fact]
    public async Task AuthorizationVersion_StartsAtOne_AndIncrementsWithoutInternalSave()
    {
        using var scope = Fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, _) = await SeedUserWithRoleAsync(sp, "veruser", "VerRole", 30);
        var store = sp.GetRequiredService<IUserAuthorizationVersionStore>();
        var uow = sp.GetRequiredService<IUnitOfWork>();
        var db = sp.GetRequiredService<ApplicationDbContext>();

        Assert.Equal(1, await store.GetAsync(user.Id));

        await store.IncrementAsync(user.Id);
        db.ChangeTracker.Clear();
        Assert.Equal(1, await store.GetAsync(user.Id));

        await store.IncrementAsync(user.Id);
        await uow.SaveChangesAsync();
        db.ChangeTracker.Clear();
        Assert.Equal(2, await store.GetAsync(user.Id));
    }

    [Fact]
    public async Task ConcurrentRbacVersionIncrements_DoNotLoseUpdates()
    {
        var connectionString = _sqlServer.CreateUniqueDatabaseConnectionString();

        await using var services1 = BuildSqlServerProvider(connectionString);
        await using var services2 = BuildSqlServerProvider(connectionString);

        await using (var scope = services1.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.MigrateAsync();
            var bootstrapper = scope.ServiceProvider.GetRequiredService<IAuthorizationStateBootstrapper>();
            await bootstrapper.EnsureCreatedAsync();
        }

        await using var scope1 = services1.CreateAsyncScope();
        await using var scope2 = services2.CreateAsyncScope();

        var states1 = scope1.ServiceProvider.GetRequiredService<IAuthorizationStateRepository>();
        var states2 = scope2.ServiceProvider.GetRequiredService<IAuthorizationStateRepository>();
        var uow1 = scope1.ServiceProvider.GetRequiredService<IUnitOfWork>();
        var uow2 = scope2.ServiceProvider.GetRequiredService<IUnitOfWork>();

        var a = await states1.GetAsync();
        var b = await states2.GetAsync();
        Assert.NotNull(a);
        Assert.NotNull(b);

        a!.IncrementRbacVersion();
        await uow1.SaveChangesAsync();

        b!.IncrementRbacVersion();
        await Assert.ThrowsAsync<ConcurrencyConflictException>(() => uow2.SaveChangesAsync());

        await scope1.DisposeAsync();
        await scope2.DisposeAsync();

        await using var verifyScope = services1.CreateAsyncScope();
        var verify = verifyScope.ServiceProvider.GetRequiredService<IAuthorizationStateRepository>();
        var finalState = await verify.GetAsync();
        Assert.Equal(2, finalState!.RbacVersion);
    }

    private static ServiceProvider BuildSqlServerProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(o => o.UseSqlServer(connectionString));
        services.AddScoped<IAuthorizationStateRepository, Persistence.Repositories.AuthorizationStateRepository>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.AddScoped<IAuthorizationStateBootstrapper, AuthorizationStateBootstrapper>();
        return services.BuildServiceProvider();
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class IdentityReaderAndOverrideTests : InfrastructureTestBase
{
    public IdentityReaderAndOverrideTests(SqlServerContainerFixture sqlServer)
        : base(sqlServer)
    {
    }

    [Fact]
    public async Task IdentityReaders_WorkAgainstAspNetTables()
    {
        using var scope = Fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, role) = await SeedUserWithRoleAsync(sp, "reader", "ReaderRole", 25);
        var users = sp.GetRequiredService<IIdentityUserReader>();
        var roles = sp.GetRequiredService<IIdentityRoleReader>();

        Assert.True(await users.UserExistsAsync(user.Id));
        Assert.True(await roles.RoleExistsAsync(role.Id));
        var roleIds = await users.GetUserRoleIdsAsync(user.Id);
        Assert.Equal(new[] { role.Id }, roleIds);
    }

    [Fact]
    public async Task UserPermissionOverride_UniquePerUserPermission()
    {
        using var scope = Fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var (user, _) = await SeedUserWithRoleAsync(sp, "ovuser", "OvRole", 40);
        var permissions = sp.GetRequiredService<IPermissionRepository>();
        var overrides = sp.GetRequiredService<IUserPermissionOverrideRepository>();
        var uow = sp.GetRequiredService<IUnitOfWork>();

        var permission = Permission.Create("Reports.Export");
        await permissions.AddAsync(permission);
        await uow.SaveChangesAsync();

        await overrides.AddAsync(UserPermissionOverride.Create(user.Id, permission.Id, PermissionEffect.Allow));
        await uow.SaveChangesAsync();

        await overrides.AddAsync(UserPermissionOverride.Create(user.Id, permission.Id, PermissionEffect.Deny));
        await Assert.ThrowsAsync<DbUpdateException>(() => uow.SaveChangesAsync());
    }

    [Fact]
    public async Task GetPermissionIdsByRoleIds_IsSetBased()
    {
        using var scope = Fixture.CreateScope();
        var sp = scope.ServiceProvider;
        var roleManager = sp.GetRequiredService<RoleManager<ApplicationRole>>();
        var permissionRepo = sp.GetRequiredService<IPermissionRepository>();
        var rpRepo = sp.GetRequiredService<IRolePermissionRepository>();
        var uow = sp.GetRequiredService<IUnitOfWork>();

        var roles = new List<ApplicationRole>();
        for (var i = 0; i < 5; i++)
        {
            var role = new ApplicationRole($"R{i}", 20 + i);
            await roleManager.CreateAsync(role);
            roles.Add(role);
            var permission = Permission.Create($"Res.Action{i}");
            await permissionRepo.AddAsync(permission);
            await rpRepo.AddAsync(RolePermission.Create(role.Id, permission.Id));
        }

        await uow.SaveChangesAsync();

        Fixture.Interceptor.Reset();
        var ids = await rpRepo.GetPermissionIdsByRoleIdsAsync(roles.Select(r => r.Id).ToArray());
        var queries = Fixture.Interceptor.Count;

        Assert.Equal(5, ids.Count);
        Assert.True(queries <= 2, $"Expected set-based query, got {queries}");
    }
}

public sealed class InfrastructureOptionsTests
{
    [Fact]
    public void AddPermixaInfrastructure_WithoutConnectionString_FailsFast()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPermixaInfrastructure());

        Assert.Contains("explicit SQL Server connection string", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddPermixaInfrastructure_DoesNotRequireJwt()
    {
        var services = new ServiceCollection();

        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = "Server=.;Database=x;Trusted_Connection=True;TrustServerCertificate=True;";
        });

        Assert.Null(services.FirstOrDefault(d => d.ServiceType == typeof(IAccessTokenGenerator)));
        Assert.Null(services.FirstOrDefault(d => d.ServiceType == typeof(RegisterUserUseCase)));
        Assert.Null(services.FirstOrDefault(d => d.ServiceType == typeof(CreatePermissionUseCase)));
    }

    [Fact]
    public void AddPermixaAuthorization_ResolvesAdministrationUseCases()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = "Server=.;Database=x;Trusted_Connection=True;TrustServerCertificate=True;";
        });
        services.AddPermixaAuthorization();

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        var scoped = scope.ServiceProvider;

        Assert.NotNull(scoped.GetRequiredService<CreatePermissionUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetPermissionByIdUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetPermissionByNameUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetPermissionsUseCase>());
        Assert.NotNull(scoped.GetRequiredService<UpdatePermissionDescriptionUseCase>());
        Assert.NotNull(scoped.GetRequiredService<AssignPermissionToRoleUseCase>());
        Assert.NotNull(scoped.GetRequiredService<RemovePermissionFromRoleUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetRolePermissionsUseCase>());
        Assert.NotNull(scoped.GetRequiredService<SetUserPermissionOverrideUseCase>());
        Assert.NotNull(scoped.GetRequiredService<RemoveUserPermissionOverrideUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetUserPermissionOverridesUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetEffectivePermissionsUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetMyEffectivePermissionsUseCase>());
        Assert.NotNull(scoped.GetRequiredService<CreateRoleUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetRoleByIdUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetRoleByNameUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetRolesUseCase>());
        Assert.NotNull(scoped.GetRequiredService<RenameRoleUseCase>());
        Assert.NotNull(scoped.GetRequiredService<ChangeRolePositionUseCase>());
        Assert.NotNull(scoped.GetRequiredService<DeleteRoleUseCase>());
        Assert.NotNull(scoped.GetRequiredService<AssignRoleToUserUseCase>());
        Assert.NotNull(scoped.GetRequiredService<RemoveRoleFromUserUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetMyRolesUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetUserRolesUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetUsersInRoleUseCase>());
        Assert.NotNull(scoped.GetRequiredService<AdminCreateUserUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetUserByIdUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetUserByEmailUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetUsersUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetUserIamDetailsUseCase>());
        Assert.NotNull(scoped.GetRequiredService<LockUserUseCase>());
        Assert.NotNull(scoped.GetRequiredService<UnlockUserUseCase>());
        Assert.NotNull(scoped.GetRequiredService<DisableUserUseCase>());
        Assert.NotNull(scoped.GetRequiredService<EnableUserUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetMySessionsUseCase>());
        Assert.NotNull(scoped.GetRequiredService<GetUserSessionsUseCase>());
        Assert.NotNull(scoped.GetRequiredService<RevokeMySessionUseCase>());
        Assert.NotNull(scoped.GetRequiredService<RevokeUserSessionUseCase>());
        Assert.NotNull(scoped.GetRequiredService<RevokeAllMySessionsUseCase>());
        Assert.NotNull(scoped.GetRequiredService<RevokeAllUserSessionsUseCase>());
        Assert.IsType<EffectivePermissionService>(scoped.GetRequiredService<IEffectivePermissionService>());
        Assert.IsType<AuthorizationHierarchyService>(scoped.GetRequiredService<IAuthorizationHierarchyService>());
    }

    [Fact]
    public void AddPermixaAuthentication_WithoutJwtIssuer_FailsFast()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPermixaAuthentication(o =>
            {
                o.Jwt.Audience = TestJwtKeys.Audience;
                o.Jwt.PrivateKeyPem = TestJwtKeys.PrivateKeyPem;
            }));

        Assert.Contains("Issuer", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddPermixaAuthentication_WithInvalidPem_FailsFast()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPermixaAuthentication(o =>
            {
                o.Jwt.Issuer = TestJwtKeys.Issuer;
                o.Jwt.Audience = TestJwtKeys.Audience;
                o.Jwt.PrivateKeyPem = "not-a-pem-key";
            }));

        Assert.Contains("PrivateKeyPem", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AddPermixaAuthentication_WithRsaBelow2048_FailsFast()
    {
        using var rsa = System.Security.Cryptography.RSA.Create(1024);
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPermixaAuthentication(o =>
            {
                o.Jwt.Issuer = TestJwtKeys.Issuer;
                o.Jwt.Audience = TestJwtKeys.Audience;
                o.Jwt.PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem();
            }));

        Assert.Contains("2048", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddPermixaAuthentication_WithInvalidMfaLifetime_FailsFast()
    {
        var services = new ServiceCollection();

        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddPermixaAuthentication(o =>
            {
                o.Jwt.Issuer = TestJwtKeys.Issuer;
                o.Jwt.Audience = TestJwtKeys.Audience;
                o.Jwt.PrivateKeyPem = TestJwtKeys.PrivateKeyPem;
                o.Mfa.ChallengeLifetime = TimeSpan.Zero;
            }));

        Assert.Contains("ChallengeLifetime", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AddPermixaInfrastructure_ResolvesMemoryPermissionCache_WithoutRedis()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = "Server=.;Database=x;Trusted_Connection=True;TrustServerCertificate=True;";
        });

        using var sp = services.BuildServiceProvider();
        Assert.IsType<Caching.MemoryPermissionCache>(sp.GetRequiredService<IPermissionCache>());
    }
}
