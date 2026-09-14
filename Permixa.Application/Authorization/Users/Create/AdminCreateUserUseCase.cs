using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Users.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Users.Create;

public sealed class AdminCreateUserUseCase
{
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly IIdentityUserCreator _userCreator;
    private readonly IIdentityUserReader _userReader;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public AdminCreateUserUseCase(
        IEffectivePermissionService effectivePermissions,
        IIdentityUserCreator userCreator,
        IIdentityUserReader userReader,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _effectivePermissions = effectivePermissions;
        _userCreator = userCreator;
        _userReader = userReader;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<IamUserDto>> ExecuteAsync(
        AdminCreateUserCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var userName = command.UserName?.Trim() ?? string.Empty;
        var email = command.Email?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(userName))
            return Result.Failure<IamUserDto>(AuthorizationErrors.InvalidUserName);
        if (string.IsNullOrWhiteSpace(email))
            return Result.Failure<IamUserDto>(AuthorizationErrors.InvalidEmail);
        if (string.IsNullOrWhiteSpace(command.Password))
            return Result.Failure<IamUserDto>(AuthorizationErrors.InvalidPassword);

        var permissionCheck = await _effectivePermissions.HasPermissionAsync(
            command.ActorUserId,
            IamPermissions.Users.Create,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<IamUserDto>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<IamUserDto>(AuthorizationErrors.MissingManagePermission);

        Result<IamUserDto>? outcome = null;

        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var created = await _userCreator.CreateAsync(userName, email, command.Password, ct);
            if (!created.Succeeded)
            {
                outcome = created.Failure switch
                {
                    IdentityUserCreationFailure.DuplicateEmail =>
                        Result.Failure<IamUserDto>(AuthenticationErrors.EmailAlreadyExists),
                    IdentityUserCreationFailure.DuplicateUserName =>
                        Result.Failure<IamUserDto>(AuthenticationErrors.UserNameAlreadyExists),
                    IdentityUserCreationFailure.InvalidPassword =>
                        Result.Failure<IamUserDto>(AuthorizationErrors.InvalidPassword),
                    _ => Result.Failure<IamUserDto>(AuthenticationErrors.EmailAlreadyExists)
                };
                return;
            }

            var now = _clock.UtcNow;
            await _audit.WriteAsync(
                IamAuditEvent.Success(
                    IamAuditEvents.Users.Created,
                    now,
                    actorUserId: command.ActorUserId,
                    targetUserId: created.UserId),
                ct);
            await _unitOfWork.SaveChangesAsync(ct);

            var user = await _userReader.GetIamUserByIdAsync(created.UserId!.Value, now, ct);
            outcome = user is null
                ? Result.Failure<IamUserDto>(AuthorizationErrors.UserNotFound)
                : Result.Success(UserMapping.ToDto(user));
        }, cancellationToken);

        return outcome ?? Result.Failure<IamUserDto>(AuthorizationErrors.UserNotFound);
    }
}
