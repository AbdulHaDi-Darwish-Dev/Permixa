using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Permixa.Infrastructure.Bootstrap;

/// <summary>
/// Secure first-time IAM bootstrap. Creates Owner via normal Identity + RolePermission data only.
/// Newly created Owner accounts are administratively trusted (<c>EmailConfirmed = true</c>).
/// Existing Owner accounts are not repaired for email confirmation on idempotent re-runs.
/// </summary>
public sealed class PermixaBootstrapper : IPermixaBootstrapper
{
    private readonly PermixaBootstrapOptions _options;
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationStateBootstrapper _authorizationStateBootstrapper;
    private readonly IamPermissionSeeder _permissionSeeder;
    private readonly IAuthorizationStateRepository _authorizationStates;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<ApplicationRole> _roleManager;

    public PermixaBootstrapper(
        IOptions<PermixaBootstrapOptions> options,
        ApplicationDbContext db,
        IAuthorizationStateBootstrapper authorizationStateBootstrapper,
        IamPermissionSeeder permissionSeeder,
        IAuthorizationStateRepository authorizationStates,
        IRolePermissionRepository rolePermissions,
        IUnitOfWork unitOfWork,
        IClock clock,
        UserManager<ApplicationUser> userManager,
        RoleManager<ApplicationRole> roleManager)
    {
        _options = options.Value;
        _db = db;
        _authorizationStateBootstrapper = authorizationStateBootstrapper;
        _permissionSeeder = permissionSeeder;
        _authorizationStates = authorizationStates;
        _rolePermissions = rolePermissions;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _userManager = userManager;
        _roleManager = roleManager;
    }

    public async Task BootstrapAsync(CancellationToken cancellationToken = default)
    {
        if (!_options.Enabled)
            return;

        ValidateEnabledOptions();

        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            await _authorizationStateBootstrapper.EnsureCreatedAsync(cancellationToken);

            var permissions = await _permissionSeeder.EnsureAsync(cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);

            var ownerRole = await EnsureOwnerRoleAsync(cancellationToken);
            var ownerUser = await EnsureOwnerUserAsync(cancellationToken);
            await EnsureOwnerMembershipAsync(ownerUser, cancellationToken);

            var addedRolePermissions = await EnsureOwnerRolePermissionsAsync(
                ownerRole.Id,
                permissions,
                cancellationToken);

            if (addedRolePermissions)
            {
                var state = await _authorizationStates.GetAsync(cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Permixa bootstrap expected AuthorizationState to exist after EnsureCreated.");

                state.IncrementRbacVersion();
            }

            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private void ValidateEnabledOptions()
    {
        if (string.IsNullOrWhiteSpace(_options.OwnerEmail))
        {
            throw new InvalidOperationException(
                "Permixa bootstrap is enabled but OwnerEmail is missing.");
        }

        if (string.IsNullOrWhiteSpace(_options.OwnerUserName))
        {
            throw new InvalidOperationException(
                "Permixa bootstrap is enabled but OwnerUserName is missing.");
        }

        if (string.IsNullOrWhiteSpace(_options.OwnerPassword))
        {
            throw new InvalidOperationException(
                "Permixa bootstrap is enabled but OwnerPassword is missing.");
        }
    }

    private async Task<ApplicationRole> EnsureOwnerRoleAsync(CancellationToken cancellationToken)
    {
        var existing = await _roleManager.FindByNameAsync(SystemRoles.Owner);
        if (existing is null)
        {
            var role = new ApplicationRole(SystemRoles.Owner, SystemRoles.OwnerRoleLevel);
            var result = await _roleManager.CreateAsync(role);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Permixa bootstrap failed to create Owner role: " + FormatIdentityErrors(result));
            }

            return role;
        }

        if (existing.RoleLevel != SystemRoles.OwnerRoleLevel)
        {
            throw new InvalidOperationException(
                $"Permixa bootstrap found role '{SystemRoles.Owner}' with RoleLevel {existing.RoleLevel}, " +
                $"expected {SystemRoles.OwnerRoleLevel}. Refusing to modify existing role authority.");
        }

        return existing;
    }

    private async Task<ApplicationUser> EnsureOwnerUserAsync(CancellationToken cancellationToken)
    {
        var email = _options.OwnerEmail!.Trim();
        var existing = await _userManager.FindByEmailAsync(email);

        if (existing is null)
        {
            var user = new ApplicationUser(_options.OwnerUserName!.Trim())
            {
                Email = email,
                // Bootstrap Owner is administratively trusted (not self-registered).
                EmailConfirmed = true
            };

            var result = await _userManager.CreateAsync(user, _options.OwnerPassword!);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Permixa bootstrap failed to create Owner user: " + FormatIdentityErrors(result));
            }

            return user;
        }

        if (!await IsCorrectlyEstablishedOwnerAsync(existing, cancellationToken))
        {
            throw new InvalidOperationException(
                $"Permixa bootstrap refused to elevate existing user '{email}'. " +
                "The account exists but is not already a correctly established Owner " +
                $"(role '{SystemRoles.Owner}' with RoleLevel {SystemRoles.OwnerRoleLevel}).");
        }

        // Username mismatch is ignored (email is the Owner identity key).
        return existing;
    }

    private async Task EnsureOwnerMembershipAsync(
        ApplicationUser ownerUser,
        CancellationToken cancellationToken)
    {
        if (await _userManager.IsInRoleAsync(ownerUser, SystemRoles.Owner))
            return;

        var result = await _userManager.AddToRoleAsync(ownerUser, SystemRoles.Owner);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException(
                "Permixa bootstrap failed to assign Owner role: " + FormatIdentityErrors(result));
        }
    }

    private async Task<bool> IsCorrectlyEstablishedOwnerAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        if (!await _userManager.IsInRoleAsync(user, SystemRoles.Owner))
            return false;

        var role = await _roleManager.FindByNameAsync(SystemRoles.Owner);
        return role is not null && role.RoleLevel == SystemRoles.OwnerRoleLevel;
    }

    private async Task<bool> EnsureOwnerRolePermissionsAsync(
        Guid ownerRoleId,
        IReadOnlyList<Permission> permissions,
        CancellationToken cancellationToken)
    {
        var permissionIds = permissions.Select(p => p.Id).ToArray();
        var existingPermissionIds = (await _rolePermissions.GetPermissionIdsByRoleIdsAsync(
                [ownerRoleId],
                cancellationToken))
            .ToHashSet();

        var added = false;
        foreach (var permissionId in permissionIds)
        {
            if (existingPermissionIds.Contains(permissionId))
                continue;

            await _rolePermissions.AddAsync(
                RolePermission.Create(ownerRoleId, permissionId, _clock.UtcNow),
                cancellationToken);
            added = true;
        }

        return added;
    }

    private static string FormatIdentityErrors(IdentityResult result) =>
        string.Join("; ", result.Errors.Select(e => e.Description));
}
