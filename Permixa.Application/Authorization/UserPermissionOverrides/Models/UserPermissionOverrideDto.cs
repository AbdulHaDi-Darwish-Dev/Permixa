using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.UserPermissionOverrides.Models;

public sealed record UserPermissionOverrideDto(
    Guid Id,
    Guid UserId,
    Guid PermissionId,
    PermissionEffect Effect,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
