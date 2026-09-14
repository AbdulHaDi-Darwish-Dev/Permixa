namespace Permixa.Application.Audit;

/// <summary>
/// Stable machine-readable IAM audit event identifiers.
/// </summary>
public static class IamAuditEvents
{
    public static class Permissions
    {
        public const string Created = "Permission.Created";
        public const string DescriptionUpdated = "Permission.DescriptionUpdated";
    }

    public static class Roles
    {
        public const string Created = "Role.Created";
        public const string Renamed = "Role.Renamed";
        public const string Repositioned = "Role.Repositioned";
        public const string Deleted = "Role.Deleted";
    }

    public static class RolePermissions
    {
        public const string Assigned = "RolePermission.Assigned";
        public const string Removed = "RolePermission.Removed";
    }

    public static class UserPermissionOverrides
    {
        public const string Set = "UserPermissionOverride.Set";
        public const string Removed = "UserPermissionOverride.Removed";
    }

    public static class UserRoles
    {
        public const string Assigned = "UserRole.Assigned";
        public const string Removed = "UserRole.Removed";
    }

    public static class Users
    {
        public const string Created = "User.Created";
        public const string Locked = "User.Locked";
        public const string Unlocked = "User.Unlocked";
        public const string Disabled = "User.Disabled";
        public const string Enabled = "User.Enabled";
    }

    public static class Sessions
    {
        public const string Revoked = "Session.Revoked";
        public const string RevokedAll = "Sessions.RevokedAll";
        public const string Logout = "Session.Logout";
    }

    public static class Credentials
    {
        public const string PasswordChanged = "Password.Changed";
        public const string PasswordResetForced = "PasswordReset.Forced";
        public const string EmailChangeRequested = "EmailChange.Requested";
        public const string EmailChangeConfirmed = "EmailChange.Confirmed";
        public const string EmailChangeAdminRequested = "EmailChange.AdminRequested";
    }

    public static class Mfa
    {
        public const string Enabled = "Mfa.Enabled";
        public const string Disabled = "Mfa.Disabled";
        public const string RecoveryCodesRegenerated = "Mfa.RecoveryCodesRegenerated";
    }
}
