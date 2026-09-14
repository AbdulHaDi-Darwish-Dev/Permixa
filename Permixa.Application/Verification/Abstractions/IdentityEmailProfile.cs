namespace Permixa.Application.Verification.Abstractions;

public sealed record IdentityEmailProfile(
    Guid UserId,
    string? Email,
    string? NormalizedEmail,
    string? PendingEmail,
    bool EmailConfirmed);
