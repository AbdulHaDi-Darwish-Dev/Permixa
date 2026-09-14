namespace Permixa.Application.Verification.Abstractions;

public interface IIdentityEmailChange
{
    Task<IdentityEmailChangeResult> ChangeEmailAsync(
        Guid userId,
        string newEmail,
        string token,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityEmailChangeResult
{
    private IdentityEmailChangeResult(
        bool succeeded,
        bool userNotFound,
        bool invalidToken,
        bool duplicateEmail)
    {
        Succeeded = succeeded;
        UserNotFound = userNotFound;
        InvalidToken = invalidToken;
        DuplicateEmail = duplicateEmail;
    }

    public bool Succeeded { get; }

    public bool UserNotFound { get; }

    public bool InvalidToken { get; }

    public bool DuplicateEmail { get; }

    public static IdentityEmailChangeResult Success() =>
        new(true, false, false, false);

    public static IdentityEmailChangeResult NotFound() =>
        new(false, true, false, false);

    public static IdentityEmailChangeResult FailedInvalidToken() =>
        new(false, false, true, false);

    public static IdentityEmailChangeResult FailedDuplicateEmail() =>
        new(false, false, false, true);

    public static IdentityEmailChangeResult Failed() =>
        new(false, false, false, false);
}
