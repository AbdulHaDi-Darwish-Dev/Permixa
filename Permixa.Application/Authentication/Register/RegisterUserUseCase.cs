using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authentication.Register;

public sealed class RegisterUserUseCase
{
    private readonly IIdentityUserCreator _userCreator;

    public RegisterUserUseCase(IIdentityUserCreator userCreator)
    {
        _userCreator = userCreator;
    }

    public async Task<Result<RegisterResult>> ExecuteAsync(
        RegisterRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.UserName))
            return Result.Failure<RegisterResult>(
                Error.Validation("Authentication.InvalidUserName", "User name is required."));

        if (string.IsNullOrWhiteSpace(request.Email))
            return Result.Failure<RegisterResult>(
                Error.Validation("Authentication.InvalidEmail", "Email is required."));

        if (string.IsNullOrWhiteSpace(request.Password))
            return Result.Failure<RegisterResult>(
                Error.Validation("Authentication.PasswordRequired", "Password is required."));

        var creation = await _userCreator.CreateAsync(
            request.UserName.Trim(),
            request.Email.Trim(),
            request.Password,
            cancellationToken);

        if (!creation.Succeeded)
        {
            return creation.Failure switch
            {
                IdentityUserCreationFailure.DuplicateEmail =>
                    Result.Failure<RegisterResult>(AuthenticationErrors.EmailAlreadyExists),
                IdentityUserCreationFailure.DuplicateUserName =>
                    Result.Failure<RegisterResult>(AuthenticationErrors.UserNameAlreadyExists),
                IdentityUserCreationFailure.InvalidPassword =>
                    Result.Failure<RegisterResult>(AuthenticationErrors.InvalidPassword),
                _ => Result.Failure<RegisterResult>(
                    Error.Failure("Authentication.RegistrationFailed", "Registration failed."))
            };
        }

        return Result.Success(new RegisterResult
        {
            UserId = creation.UserId!.Value,
            UserName = request.UserName.Trim(),
            Email = request.Email.Trim(),
            EmailConfirmed = creation.EmailConfirmed
        });
    }
}
