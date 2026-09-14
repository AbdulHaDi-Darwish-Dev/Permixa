namespace Permixa.Application.Verification.Abstractions;

/// <summary>
/// Identity password reset bound to a reset token (no unbound ChangePassword).
/// </summary>
public interface IIdentityPasswordReset
{
    Task<IdentityPasswordResetResult> ResetPasswordAsync(
        Guid userId,
        string token,
        string newPassword,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityPasswordResetResult
{
    private IdentityPasswordResetResult(bool succeeded, bool invalidToken, bool invalidPassword)
    {
        Succeeded = succeeded;
        InvalidToken = invalidToken;
        InvalidPassword = invalidPassword;
    }

    public bool Succeeded { get; }

    public bool InvalidToken { get; }

    public bool InvalidPassword { get; }

    public static IdentityPasswordResetResult Success() =>
        new(true, false, false);

    public static IdentityPasswordResetResult FailedInvalidToken() =>
        new(false, true, false);

    public static IdentityPasswordResetResult FailedInvalidPassword() =>
        new(false, false, true);

    public static IdentityPasswordResetResult Failed() =>
        new(false, false, false);
}
