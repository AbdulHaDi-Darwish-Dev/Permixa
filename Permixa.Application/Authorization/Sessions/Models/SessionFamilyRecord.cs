namespace Permixa.Application.Authorization.Sessions.Models;

/// <summary>
/// Set-based projection of one refresh-token family (one logical session).
/// </summary>
public sealed record SessionFamilyRecord(
    Guid FamilyId,
    DateTime CreatedAtUtc,
    DateTime ExpiresAtUtc);
