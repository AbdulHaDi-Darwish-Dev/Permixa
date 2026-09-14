namespace Permixa.Application.Identity.Abstractions;

/// <summary>
/// Live account gates rechecked immediately before MFA credential issuance.
/// </summary>
public sealed record IdentityMfaLoginGate(
    Guid UserId,
    bool IsDisabled,
    bool IsLocked,
    bool EmailConfirmed,
    bool TwoFactorEnabled);
