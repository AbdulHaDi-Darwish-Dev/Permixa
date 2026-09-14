using Permixa.Domain.Verification;

namespace Permixa.Application.Verification.Abstractions;

/// <summary>
/// Provider-independent verification secret generation and validation.
/// Infrastructure will adapt ASP.NET Core Identity token providers.
/// </summary>
public interface IVerificationTokenProvider
{
    Task<string> GenerateAsync(
        Guid userId,
        VerificationPurpose purpose,
        VerificationMethod method,
        CancellationToken cancellationToken = default);

    Task<bool> ValidateAsync(
        Guid userId,
        VerificationPurpose purpose,
        VerificationMethod method,
        string token,
        CancellationToken cancellationToken = default);
}
