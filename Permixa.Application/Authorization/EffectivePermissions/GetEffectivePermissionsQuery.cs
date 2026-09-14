namespace Permixa.Application.Authorization.EffectivePermissions;

public sealed record GetEffectivePermissionsQuery(Guid ActorUserId, Guid TargetUserId);
