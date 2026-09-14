namespace Permixa.Application.Authentication.Abstractions;

/// <summary>
/// Creates ASP.NET Core Identity users without exposing Identity types to Application.
/// </summary>
public interface IIdentityUserCreator
{
    Task<IdentityUserCreationResult> CreateAsync(
        string userName,
        string email,
        string password,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityUserCreationResult
{
    private IdentityUserCreationResult(
        bool succeeded,
        Guid? userId,
        bool emailConfirmed,
        IdentityUserCreationFailure? failure)
    {
        Succeeded = succeeded;
        UserId = userId;
        EmailConfirmed = emailConfirmed;
        Failure = failure;
    }

    public bool Succeeded { get; }

    public Guid? UserId { get; }

    public bool EmailConfirmed { get; }

    public IdentityUserCreationFailure? Failure { get; }

    public static IdentityUserCreationResult Success(Guid userId, bool emailConfirmed) =>
        new(true, userId, emailConfirmed, null);

    public static IdentityUserCreationResult Failed(IdentityUserCreationFailure failure) =>
        new(false, null, false, failure);
}

public enum IdentityUserCreationFailure
{
    DuplicateEmail,
    DuplicateUserName,
    InvalidPassword,
    Failed
}
