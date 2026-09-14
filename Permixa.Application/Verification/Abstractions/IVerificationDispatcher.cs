using Permixa.Application.Verification.Models;

namespace Permixa.Application.Verification.Abstractions;

/// <summary>
/// Dispatches verification payloads through Email/SMS without knowing providers.
/// </summary>
public interface IVerificationDispatcher
{
    Task DispatchAsync(
        VerificationDeliveryRequest request,
        CancellationToken cancellationToken = default);
}
