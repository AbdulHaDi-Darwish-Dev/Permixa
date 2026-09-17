using PermixaApp.Application.Authorization;
using Permixa.Application.Authorization;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;

namespace PermixaApp.Api.Hosting;

/// <summary>
/// Privileged development / application initialization for host-owned permissions.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> a normal authorization mutation API. It must not be exposed over HTTP
/// or treated as general-purpose administration. Prefer Permixa use cases
/// (<c>AssignPermissionToRoleUseCase</c>, etc.) for runtime RBAC changes.
/// </para>
/// <para>
/// Hierarchy and actor permission checks are intentionally skipped: Owner cannot grant onto
/// Owner via hierarchy-gated use cases (same <c>RoleLevel</c> cannot control itself). This
/// path exists only for Development startup seeding under <c>Permixa:AppSeed:Enabled</c>.
/// </para>
/// <para>
/// Permission create/grant and <see cref="AuthorizationState.RbacVersion"/> increment share
/// one unit of work so authorization cache invalidation stays correct.
/// </para>
/// </remarks>
public sealed class AppPermissionSeeder
{
    private readonly IPermissionRepository _permissions;
    private readonly IRolePermissionRepository _rolePermissions;
    private readonly IAuthorizationStateRepository _authorizationStates;
    private readonly IIdentityRoleReader _roles;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public AppPermissionSeeder(
        IPermissionRepository permissions,
        IRolePermissionRepository rolePermissions,
        IAuthorizationStateRepository authorizationStates,
        IIdentityRoleReader roles,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _permissions = permissions;
        _rolePermissions = rolePermissions;
        _authorizationStates = authorizationStates;
        _roles = roles;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var ownerRole = await _roles.GetByNameAsync(PermixaRoles.Owner, cancellationToken);
        if (ownerRole is null)
            return;

        var now = _clock.UtcNow;
        var added = false;

        foreach (var name in AppPermissions.All)
        {
            var permission = await _permissions.GetByNameAsync(name, cancellationToken);
            if (permission is null)
            {
                permission = Permission.Create(name, "REFERENCE / app permission", createdAtUtc: now);
                await _permissions.AddAsync(permission, cancellationToken);
                added = true;
            }

            if (!await _rolePermissions.ExistsAsync(ownerRole.Id, permission.Id, cancellationToken))
            {
                await _rolePermissions.AddAsync(
                    RolePermission.Create(ownerRole.Id, permission.Id, now),
                    cancellationToken);
                added = true;
            }
        }

        if (!added)
            return;

        var state = await _authorizationStates.GetAsync(cancellationToken)
            ?? throw new InvalidOperationException(
                "AuthorizationState is missing after Permixa bootstrap. " +
                "App permission seed refuses to grant permissions without IncrementRbacVersion, " +
                "which would leave authorization caches stale. Ensure Development startup runs " +
                "IPermixaBootstrapper before AppPermissionSeeder.");

        state.IncrementRbacVersion();
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
