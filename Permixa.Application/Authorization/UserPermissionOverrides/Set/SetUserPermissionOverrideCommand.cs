using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.UserPermissionOverrides.Set;

public sealed record SetUserPermissionOverrideCommand(
    Guid ActorUserId,
    Guid TargetUserId,
    Guid PermissionId,
    PermissionEffect Effect);
