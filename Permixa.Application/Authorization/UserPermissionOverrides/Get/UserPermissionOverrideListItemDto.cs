using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.UserPermissionOverrides.Get;

public sealed record UserPermissionOverrideListItemDto(
    Guid PermissionId,
    string PermissionName,
    PermissionEffect Effect);
