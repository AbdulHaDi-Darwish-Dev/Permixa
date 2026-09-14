namespace Permixa.Application.Authorization.UserPermissionOverrides.Get;

public sealed record GetUserPermissionOverridesQuery(Guid ActorUserId, Guid TargetUserId);
