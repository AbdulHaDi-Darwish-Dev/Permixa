namespace Permixa.Application.Authorization.Permissions.Update;

public sealed record UpdatePermissionDescriptionCommand(
    Guid ActorUserId,
    Guid PermissionId,
    string? Description);
