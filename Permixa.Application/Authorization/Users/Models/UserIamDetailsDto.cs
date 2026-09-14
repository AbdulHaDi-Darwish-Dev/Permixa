using Permixa.Application.Authorization.Roles.Models;
using Permixa.Application.Authorization.UserPermissionOverrides.Get;

namespace Permixa.Application.Authorization.Users.Models;

public sealed record UserIamDetailsDto(
    IamUserDto User,
    IReadOnlyList<RoleDto> Roles,
    IReadOnlyList<UserPermissionOverrideListItemDto> Overrides,
    IReadOnlyList<string> EffectivePermissions);
