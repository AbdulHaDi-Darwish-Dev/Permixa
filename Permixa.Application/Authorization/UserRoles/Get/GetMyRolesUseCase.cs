using Permixa.Application.Authorization.Roles.Models;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.UserRoles.Get;

public sealed class GetMyRolesUseCase
{
    private readonly IIdentityUserReader _userReader;
    private readonly IIdentityUserRoleReader _userRoles;

    public GetMyRolesUseCase(
        IIdentityUserReader userReader,
        IIdentityUserRoleReader userRoles)
    {
        _userReader = userReader;
        _userRoles = userRoles;
    }

    public async Task<Result<IReadOnlyList<RoleDto>>> ExecuteAsync(
        GetMyRolesQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.UserId == Guid.Empty || !await _userReader.UserExistsAsync(query.UserId, cancellationToken))
            return Result.Failure<IReadOnlyList<RoleDto>>(AuthorizationErrors.UserNotFound);

        var roles = await _userRoles.GetRolesForUserAsync(query.UserId, cancellationToken);
        var dtos = roles
            .OrderBy(r => r.RoleLevel)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .Select(RoleMapping.ToDto)
            .ToList();

        return Result.Success<IReadOnlyList<RoleDto>>(dtos);
    }
}
