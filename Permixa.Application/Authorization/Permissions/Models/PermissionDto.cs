namespace Permixa.Application.Authorization.Permissions.Models;

public sealed record PermissionDto(
    Guid Id,
    string Name,
    string? Description,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc);
