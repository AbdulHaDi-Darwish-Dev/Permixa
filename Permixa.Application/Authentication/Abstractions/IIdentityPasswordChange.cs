namespace Permixa.Application.Authentication.Abstractions;

public interface IIdentityPasswordChange
{
    Task<bool> CheckPasswordAsync(
        Guid userId,
        string password,
        CancellationToken cancellationToken = default);

    Task<IdentityPasswordChangeResult> ChangePasswordAsync(
        Guid userId,
        string currentPassword,
        string newPassword,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityPasswordChangeResult
{
    private IdentityPasswordChangeResult(
        bool succeeded,
        bool userNotFound,
        bool invalidCurrentPassword,
        bool invalidNewPassword)
    {
        Succeeded = succeeded;
        UserNotFound = userNotFound;
        InvalidCurrentPassword = invalidCurrentPassword;
        InvalidNewPassword = invalidNewPassword;
    }

    public bool Succeeded { get; }

    public bool UserNotFound { get; }

    public bool InvalidCurrentPassword { get; }

    public bool InvalidNewPassword { get; }

    public static IdentityPasswordChangeResult Success() =>
        new(true, false, false, false);

    public static IdentityPasswordChangeResult NotFound() =>
        new(false, true, false, false);

    public static IdentityPasswordChangeResult FailedCurrentPassword() =>
        new(false, false, true, false);

    public static IdentityPasswordChangeResult FailedNewPassword() =>
        new(false, false, false, true);

    public static IdentityPasswordChangeResult Failed() =>
        new(false, false, false, false);
}
