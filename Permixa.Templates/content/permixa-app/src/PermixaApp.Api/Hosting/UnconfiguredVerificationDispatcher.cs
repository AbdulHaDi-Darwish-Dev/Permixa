using Permixa.Application.Verification.Abstractions;
using Permixa.Application.Verification.Models;

namespace PermixaApp.Api.Hosting;

/// <summary>
/// Placeholder dispatcher for hosts without email delivery (--resend / custom).
/// Satisfies DI for <c>AddPermixaVerification</c> + authorization admin use cases.
/// Calling it fails fast with configuration guidance.
/// </summary>
public sealed class UnconfiguredVerificationDispatcher : IVerificationDispatcher
{
    public Task DispatchAsync(
        VerificationDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new InvalidOperationException(
            "Verification delivery is not configured. Add --resend (or register " +
            "AddPermixaEmailDelivery + an IEmailSender / custom IVerificationDispatcher).");
    }
}
