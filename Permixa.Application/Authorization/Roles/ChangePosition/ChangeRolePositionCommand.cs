using Permixa.Application.Authorization.Hierarchy;

namespace Permixa.Application.Authorization.Roles.ChangePosition;

public sealed record ChangeRolePositionCommand(
    Guid ActorUserId,
    Guid RoleId,
    Guid ReferenceRoleId,
    RolePlacement Placement);
