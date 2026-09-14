using Permixa.Domain.Verification;

namespace Permixa.Application.Verification.Models;

/// <summary>
/// In-memory delivery payload for a verification challenge.
/// The raw verification value exists only transiently for delivery and must not be persisted as domain state.
/// </summary>
public sealed record VerificationDeliveryRequest(
    Guid ChallengeId,
    string Destination,
    VerificationPurpose Purpose,
    VerificationMethod Method,
    VerificationChannel Channel,
    string RawVerificationValue,
    DateTime ExpiresAtUtc);
