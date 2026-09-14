using System.ComponentModel.DataAnnotations;
using Permixa.Application.Authentication;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Application.Verification.Abstractions;
using Permixa.Domain.Verification;

namespace Permixa.Application.Verification.EmailChange;

/// <summary>
/// Shared pending-email mutation + EmailChange challenge issuance.
/// </summary>
public sealed class PendingEmailChangeService
{
    private static readonly EmailAddressAttribute EmailFormat = new();

    private readonly IIdentityUserEmailReader _emails;
    private readonly IIdentityUserWriter _writer;
    private readonly IVerificationChallengeRepository _challenges;
    private readonly VerificationChallengeIssuer _issuer;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public PendingEmailChangeService(
        IIdentityUserEmailReader emails,
        IIdentityUserWriter writer,
        IVerificationChallengeRepository challenges,
        VerificationChallengeIssuer issuer,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _emails = emails;
        _writer = writer;
        _challenges = challenges;
        _issuer = issuer;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result> RequestAsync(
        Guid userId,
        string newEmail,
        CancellationToken cancellationToken = default)
    {
        var trimmed = newEmail.Trim();
        if (string.IsNullOrWhiteSpace(trimmed) || trimmed.Length > 256 || !EmailFormat.IsValid(trimmed))
            return Result.Failure(AuthenticationErrors.InvalidEmail);

        var profile = await _emails.GetEmailProfileAsync(userId, cancellationToken);
        if (profile is null)
            return Result.Failure(VerificationErrors.UserNotFound);

        var normalized = trimmed.ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(profile.NormalizedEmail)
            && string.Equals(profile.NormalizedEmail, normalized, StringComparison.Ordinal))
        {
            return Result.Failure(AuthenticationErrors.EmailUnchanged);
        }

        var pendingMatches = !string.IsNullOrWhiteSpace(profile.PendingEmail)
            && string.Equals(profile.PendingEmail.ToUpperInvariant(), normalized, StringComparison.Ordinal);

        if (!pendingMatches)
        {
            if (await _emails.IsEmailClaimedByAnotherUserAsync(userId, trimmed, cancellationToken))
                return Result.Failure(AuthenticationErrors.EmailAlreadyExists);

            await _writer.SetPendingEmailAsync(userId, trimmed, cancellationToken);
            await _unitOfWork.SaveChangesAsync(cancellationToken);
            await _challenges.InvalidateOpenForPurposeAsync(
                userId,
                VerificationPurpose.EmailChange,
                _clock.UtcNow,
                cancellationToken);
        }
        else if (await _emails.IsEmailClaimedByAnotherUserAsync(userId, trimmed, cancellationToken))
        {
            return Result.Failure(AuthenticationErrors.EmailAlreadyExists);
        }

        return await _issuer.IssueAsync(
            userId,
            VerificationPurpose.EmailChange,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            trimmed,
            cancellationToken);
    }
}
