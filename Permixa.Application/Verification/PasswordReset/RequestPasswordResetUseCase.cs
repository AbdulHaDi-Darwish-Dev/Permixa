using Permixa.Application.Common.Results;
using Permixa.Application.Verification.Abstractions;
using Permixa.Domain.Verification;

namespace Permixa.Application.Verification.PasswordReset;

public sealed class RequestPasswordResetUseCase
{
    private readonly IIdentityUserEmailReader _emails;
    private readonly VerificationChallengeIssuer _issuer;

    public RequestPasswordResetUseCase(
        IIdentityUserEmailReader emails,
        VerificationChallengeIssuer issuer)
    {
        _emails = emails;
        _issuer = issuer;
    }

    public async Task<Result> ExecuteAsync(
        RequestPasswordResetRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Email))
            return Result.Success();

        var userId = await _emails.FindUserIdByEmailAsync(request.Email.Trim(), cancellationToken);
        if (userId is null)
            return Result.Success();

        var email = await _emails.GetEmailAsync(userId.Value, cancellationToken);
        if (string.IsNullOrWhiteSpace(email))
            return Result.Success();

        await _issuer.IssueBestEffortAntiEnumerationAsync(
            userId.Value,
            VerificationPurpose.PasswordReset,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            email,
            cancellationToken);

        return Result.Success();
    }
}
