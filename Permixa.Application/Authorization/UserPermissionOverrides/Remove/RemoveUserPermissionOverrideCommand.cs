namespace Permixa.Application.Authorization.UserPermissionOverrides.Remove;

public sealed record RemoveUserPermissionOverrideCommand(
    Guid ActorUserId,
    Guid TargetUserId,
    Guid PermissionId);
