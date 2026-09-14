namespace Permixa.Application.Authorization.UserRoles.Get;

public sealed record IamUserListItemDto(Guid UserId, string? UserName, string? Email);
