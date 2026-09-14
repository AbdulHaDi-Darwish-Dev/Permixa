using Permixa.Application.Common.Results;

namespace Permixa.Application.Authorization;

/// <summary>
/// Authorization-specific expected failures.
/// </summary>
public static class AuthorizationErrors
{
    public static readonly Error PermissionNotFound =
        Error.NotFound("Authorization.PermissionNotFound", "The permission was not found.");

    public static readonly Error PermissionAlreadyExists =
        Error.Conflict("Authorization.PermissionAlreadyExists", "A permission with the same name already exists.");

    public static readonly Error InvalidPermission =
        Error.Validation("Authorization.InvalidPermission", "The permission name is invalid.");

    public static readonly Error RoleNotFound =
        Error.NotFound("Authorization.RoleNotFound", "The role was not found.");

    public static readonly Error UserNotFound =
        Error.NotFound("Authorization.UserNotFound", "The user was not found.");

    public static readonly Error RolePermissionAlreadyExists =
        Error.Conflict("Authorization.RolePermissionAlreadyExists", "The role already has this permission.");

    public static readonly Error RolePermissionNotFound =
        Error.NotFound("Authorization.RolePermissionNotFound", "The role permission relationship was not found.");

    public static readonly Error OverrideNotFound =
        Error.NotFound("Authorization.OverrideNotFound", "The user permission override was not found.");

    public static readonly Error OverrideUnchanged =
        Error.Conflict("Authorization.OverrideUnchanged", "The user permission override already has the requested effect.");

    public static readonly Error HierarchyViolation =
        Error.Forbidden("Authorization.HierarchyViolation", "The actor is not hierarchically authorized for this operation.");

    public static readonly Error CannotManageSelf =
        Error.Forbidden("Authorization.CannotManageSelf", "Administrative hierarchy operations on oneself are not allowed.");

    public static readonly Error MissingManagePermission =
        Error.Forbidden("Authorization.MissingManagePermission", "The actor does not have the required management permission.");

    public static readonly Error AuthorizationStateUnavailable =
        Error.Failure("Authorization.StateUnavailable", "The global authorization state is unavailable.");

    public static readonly Error RoleAlreadyExists =
        Error.Conflict("Authorization.RoleAlreadyExists", "A role with the same name already exists.");

    public static readonly Error InvalidRoleName =
        Error.Validation("Authorization.InvalidRoleName", "The role name is invalid.");

    public static readonly Error InvalidRolePlacement =
        Error.Validation("Authorization.InvalidRolePlacement", "The role placement is invalid.");

    public static readonly Error OwnerProtected =
        Error.Forbidden("Authorization.OwnerProtected", "The Owner role cannot be modified through this operation.");

    public static readonly Error RoleHasUsers =
        Error.Conflict("Authorization.RoleHasUsers", "The role cannot be deleted while users are assigned.");

    public static readonly Error RoleHasPermissions =
        Error.Conflict("Authorization.RoleHasPermissions", "The role cannot be deleted while permissions are assigned.");

    public static readonly Error HierarchyLevelSpaceExhausted =
        Error.Failure(
            "Authorization.HierarchyLevelSpaceExhausted",
            "No free RoleLevel remains in the weaker direction.");

    public static readonly Error InvalidLockoutEnd =
        Error.Validation(
            "Authorization.InvalidLockoutEnd",
            "Lockout end must be a future UTC time.");

    public static readonly Error InvalidPaging =
        Error.Validation(
            "Authorization.InvalidPaging",
            "Page must be at least 1 and PageSize must be between 1 and 100.");

    public static readonly Error InvalidUserName =
        Error.Validation("Authorization.InvalidUserName", "User name is required.");

    public static readonly Error InvalidEmail =
        Error.Validation("Authorization.InvalidEmail", "Email is required.");

    public static readonly Error InvalidPassword =
        Error.Validation("Authorization.InvalidPassword", "The password does not meet the required policy.");

    public static readonly Error SessionNotFound =
        Error.NotFound("Authorization.SessionNotFound", "The session was not found.");
}
