using Permixa.Application.Common.Results;
using Permixa.Application.Verification.Abstractions;
using Permixa.Domain.Verification;

namespace Permixa.Application.Verification.EmailConfirmation;

public sealed class RequestEmailConfirmationUseCase
{
    private readonly IIdentityUserEmailReader _emails;
    private readonly VerificationChallengeIssuer _issuer;

    public RequestEmailConfirmationUseCase(
        IIdentityUserEmailReader emails,
        VerificationChallengeIssuer issuer)
    {
        _emails = emails;
        _issuer = issuer;
    }

    public async Task<Result<RequestEmailConfirmationResult>> ExecuteAsync(
        RequestEmailConfirmationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.UserId == Guid.Empty)
            return Result.Failure<RequestEmailConfirmationResult>(VerificationErrors.UserNotFound);

        if (request.Method is not (VerificationMethod.Otp or VerificationMethod.UrlToken))
            return Result.Failure<RequestEmailConfirmationResult>(VerificationErrors.UnsupportedMethod);

        var email = await _emails.GetEmailAsync(request.UserId, cancellationToken);
        if (email is null)
            return Result.Failure<RequestEmailConfirmationResult>(VerificationErrors.UserNotFound);

        if (await _emails.IsEmailConfirmedAsync(request.UserId, cancellationToken))
            return Result.Success(RequestEmailConfirmationResult.ConfirmedAlready());

        var issued = await _issuer.IssueAsync(
            request.UserId,
            VerificationPurpose.EmailConfirmation,
            request.Method,
            VerificationChannel.Email,
            email,
            cancellationToken);

        if (issued.IsFailure)
            return Result.Failure<RequestEmailConfirmationResult>(issued.Error!);

        return Result.Success(RequestEmailConfirmationResult.Issued(issued.Value));
    }
}
