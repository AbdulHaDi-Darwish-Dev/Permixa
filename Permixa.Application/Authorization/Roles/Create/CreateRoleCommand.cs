using Permixa.Application.Authorization.Hierarchy;

namespace Permixa.Application.Authorization.Roles.Create;

public sealed record CreateRoleCommand(
    Guid ActorUserId,
    string Name,
    Guid ReferenceRoleId,
    RolePlacement Placement);
