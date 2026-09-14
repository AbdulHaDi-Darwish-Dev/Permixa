using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Models;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.EffectivePermissions;

/// <summary>
/// Builds versioned authorization snapshots with optional cache orchestration.
/// Cached snapshots are accepted only when UserVersion and RbacVersion still match current state.
/// </summary>
public sealed class EffectivePermissionService : IEffectivePermissionService
{
    private readonly IIdentityUserReader _userReader;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IUserPermissionOverrideRepository _overrideRepository;
    private readonly IAuthorizationStateRepository _authorizationStateRepository;
    private readonly IUserAuthorizationVersionStore _userAuthorizationVersionStore;
    private readonly IRoleHierarchyReader _roleHierarchyReader;
    private readonly IPermissionCache _permissionCache;

    public EffectivePermissionService(
        IIdentityUserReader userReader,
        IPermissionRepository permissionRepository,
        IUserPermissionOverrideRepository overrideRepository,
        IAuthorizationStateRepository authorizationStateRepository,
        IUserAuthorizationVersionStore userAuthorizationVersionStore,
        IRoleHierarchyReader roleHierarchyReader,
        IPermissionCache permissionCache)
    {
        _userReader = userReader;
        _permissionRepository = permissionRepository;
        _overrideRepository = overrideRepository;
        _authorizationStateRepository = authorizationStateRepository;
        _userAuthorizationVersionStore = userAuthorizationVersionStore;
        _roleHierarchyReader = roleHierarchyReader;
        _permissionCache = permissionCache;
    }

    public async Task<Result<AuthorizationSnapshot>> GetAuthorizationSnapshotAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || !await _userReader.UserExistsAsync(userId, cancellationToken))
            return Result.Failure<AuthorizationSnapshot>(AuthorizationErrors.UserNotFound);

        var userVersion = await _userAuthorizationVersionStore.GetAsync(userId, cancellationToken);
        var rbacVersion = await GetCurrentRbacVersionAsync(cancellationToken);

        var cached = await _permissionCache.GetAsync(userId, cancellationToken);
        if (cached is not null
            && cached.UserVersion == userVersion
            && cached.RbacVersion == rbacVersion)
        {
            return Result.Success(cached);
        }

        var effectiveLevel = await _roleHierarchyReader.GetEffectiveUserLevelAsync(userId, cancellationToken);
        var permissions = await ResolveEffectivePermissionsAsync(userId, cancellationToken);

        var snapshot = new AuthorizationSnapshot(
            userVersion,
            rbacVersion,
            effectiveLevel,
            permissions);

        await _permissionCache.SetAsync(userId, snapshot, cancellationToken);
        return Result.Success(snapshot);
    }

    public async Task<Result<bool>> HasPermissionAsync(
        Guid userId,
        string permissionName,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(permissionName))
            return Result.Failure<bool>(AuthorizationErrors.InvalidPermission);

        var snapshotResult = await GetAuthorizationSnapshotAsync(userId, cancellationToken);
        if (snapshotResult.IsFailure)
            return Result.Failure<bool>(snapshotResult.Error!);

        return Result.Success(snapshotResult.Value.HasPermission(permissionName.Trim()));
    }

    private async Task<int> GetCurrentRbacVersionAsync(CancellationToken cancellationToken)
    {
        var state = await _authorizationStateRepository.GetAsync(cancellationToken);
        return state?.RbacVersion ?? AuthorizationState.InitialRbacVersion;
    }

    private async Task<IReadOnlySet<string>> ResolveEffectivePermissionsAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var roleIds = await _userReader.GetUserRoleIdsAsync(userId, cancellationToken);

        IReadOnlyCollection<Permission> rolePermissions = roleIds.Count == 0
            ? Array.Empty<Permission>()
            : await _permissionRepository.GetByRoleIdsAsync(roleIds, cancellationToken);

        var roleGrantedNames = rolePermissions
            .Select(p => p.Name)
            .ToHashSet(StringComparer.Ordinal);

        var permissionsById = new Dictionary<Guid, Permission>();
        foreach (var permission in rolePermissions)
            permissionsById[permission.Id] = permission;

        var overrides = await _overrideRepository.GetByUserIdAsync(userId, cancellationToken);

        var missingPermissionIds = overrides
            .Select(o => o.PermissionId)
            .Where(id => !permissionsById.ContainsKey(id))
            .Distinct()
            .ToArray();

        if (missingPermissionIds.Length > 0)
        {
            var missingPermissions = await _permissionRepository.GetByIdsAsync(
                missingPermissionIds,
                cancellationToken);

            foreach (var permission in missingPermissions)
                permissionsById[permission.Id] = permission;
        }

        var overrideByPermissionName = new Dictionary<string, PermissionEffect>(StringComparer.Ordinal);
        foreach (var permissionOverride in overrides)
        {
            if (!permissionsById.TryGetValue(permissionOverride.PermissionId, out var permission))
                continue;

            overrideByPermissionName[permission.Name] = permissionOverride.Effect;
        }

        var candidates = new HashSet<string>(roleGrantedNames, StringComparer.Ordinal);
        foreach (var name in overrideByPermissionName.Keys)
            candidates.Add(name);

        var effective = new HashSet<string>(StringComparer.Ordinal);
        foreach (var name in candidates)
        {
            PermissionEffect? overrideEffect = overrideByPermissionName.TryGetValue(name, out var effect)
                ? effect
                : null;

            var roleGrants = roleGrantedNames.Contains(name);
            if (PermissionAuthorizationResolver.IsAllowed(roleGrants, overrideEffect))
                effective.Add(name);
        }

        return effective;
    }
}
