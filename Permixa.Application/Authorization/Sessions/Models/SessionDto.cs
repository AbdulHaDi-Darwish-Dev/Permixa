namespace Permixa.Application.Authorization.Sessions.Models;

public sealed record SessionDto(
    Guid FamilyId,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc,
    bool IsCurrent);
