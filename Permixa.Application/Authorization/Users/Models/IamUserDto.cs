namespace Permixa.Application.Authorization.Users.Models;

public sealed record IamUserDto(
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
