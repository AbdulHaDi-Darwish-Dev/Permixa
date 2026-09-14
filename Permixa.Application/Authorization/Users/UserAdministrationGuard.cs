using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Users;

internal static class UserAdministrationGuard
{
    public static async Task<Result?> EnsurePermissionAsync(
        IEffectivePermissionService permissions,
        Guid actorUserId,
        string permission,
        CancellationToken cancellationToken)
    {
        var check = await permissions.HasPermissionAsync(actorUserId, permission, cancellationToken);
        if (check.IsFailure)
            return Result.Failure(check.Error!);

        return check.Value ? null : Result.Failure(AuthorizationErrors.MissingManagePermission);
    }

    public static Result? RejectSelf(Guid actorUserId, Guid targetUserId) =>
        actorUserId == targetUserId ? Result.Failure(AuthorizationErrors.CannotManageSelf) : null;

    public static async Task<Result?> EnsureCanManageAsync(
        IAuthorizationHierarchyService hierarchy,
        Guid actorUserId,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        return await hierarchy.CanManageUserAsync(actorUserId, targetUserId, cancellationToken)
            ? null
            : Result.Failure(AuthorizationErrors.HierarchyViolation);
    }

    public static async Task<Result?> RejectOwnerAsync(
        IIdentityUserReader users,
        Guid targetUserId,
        CancellationToken cancellationToken)
    {
        return await users.HasRoleNameAsync(targetUserId, PermixaRoles.Owner, cancellationToken)
            ? Result.Failure(AuthorizationErrors.OwnerProtected)
            : null;
    }
}
