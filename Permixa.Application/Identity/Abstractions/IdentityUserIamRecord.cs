namespace Permixa.Application.Identity.Abstractions;

public sealed record IdentityUserIamRecord(
    Guid Id,
    string? UserName,
    string? Email,
    bool EmailConfirmed,
    bool IsDisabled,
    bool IsLocked,
    DateTime? LockoutEndUtc,
    int? EffectiveRoleLevel,
    bool TwoFactorEnabled,
    string? PendingEmail);
