namespace Permixa.Application.Authorization;

/// <summary>
/// Framework-owned IAM administrative permissions.
/// Consuming applications define their own business permissions separately.
/// </summary>
public static class IamPermissions
{
    public static class Permissions
    {
        public const string Create = "Iam.Permissions.Create";
        public const string Read = "Iam.Permissions.Read";
        public const string Update = "Iam.Permissions.Update";
    }

    public static class RolePermissions
    {
        public const string Manage = "Iam.RolePermissions.Manage";
    }

    public static class UserPermissionOverrides
    {
        public const string Manage = "Iam.UserPermissionOverrides.Manage";
    }

    public static class Roles
    {
        public const string Read = "Iam.Roles.Read";
        public const string Create = "Iam.Roles.Create";
        public const string Update = "Iam.Roles.Update";
        public const string Delete = "Iam.Roles.Delete";
    }

    public static class UserRoles
    {
        public const string Manage = "Iam.UserRoles.Manage";
    }

    public static class Users
    {
        public const string Read = "Iam.Users.Read";
        public const string Create = "Iam.Users.Create";
        public const string Lock = "Iam.Users.Lock";
        public const string Disable = "Iam.Users.Disable";
        public const string ChangeEmail = "Iam.Users.ChangeEmail";
        public const string ForcePasswordReset = "Iam.Users.ForcePasswordReset";
    }

    public static class Sessions
    {
        public const string Read = "Iam.Sessions.Read";
        public const string Revoke = "Iam.Sessions.Revoke";
    }

    public static class Audit
    {
        public const string Read = "Iam.Audit.Read";
    }

    /// <summary>
    /// Complete Permixa-owned IAM permission catalog for seeding.
    /// </summary>
    public static IReadOnlyList<string> All { get; } =
    [
        Permissions.Create,
        Permissions.Read,
        Permissions.Update,
        RolePermissions.Manage,
        UserPermissionOverrides.Manage,
        Roles.Read,
        Roles.Create,
        Roles.Update,
        Roles.Delete,
        UserRoles.Manage,
        Users.Read,
        Users.Create,
        Users.Lock,
        Users.Disable,
        Users.ChangeEmail,
        Users.ForcePasswordReset,
        Sessions.Read,
        Sessions.Revoke,
        Audit.Read
    ];
}
