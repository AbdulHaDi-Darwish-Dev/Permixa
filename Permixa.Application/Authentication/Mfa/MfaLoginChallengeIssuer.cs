using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Common;
using Permixa.Application.Common.Abstractions;
using Permixa.Domain.Authentication;
using Microsoft.Extensions.Options;

namespace Permixa.Application.Authentication.Mfa;

public sealed class MfaLoginChallengeIssuer
{
    private readonly IMfaLoginChallengeRepository _challenges;
    private readonly IRefreshTokenCrypto _crypto;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;
    private readonly IPersistenceExceptionClassifier _persistence;
    private readonly PermixaMfaOptions _options;

    public MfaLoginChallengeIssuer(
        IMfaLoginChallengeRepository challenges,
        IRefreshTokenCrypto crypto,
        IUnitOfWork unitOfWork,
        IClock clock,
        IPersistenceExceptionClassifier persistence,
        IOptions<PermixaMfaOptions> options)
    {
        _challenges = challenges;
        _crypto = crypto;
        _unitOfWork = unitOfWork;
        _clock = clock;
        _persistence = persistence;
        _options = options.Value;
    }

    public async Task<(string Proof, DateTime ExpiresAtUtc)> IssueAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var raw = _crypto.GenerateRawToken();
        var hash = _crypto.HashToken(raw);
        var now = _clock.UtcNow;
        var expires = now.Add(_options.ChallengeLifetime);

        // Unique/concurrency failures abort the SQL transaction. Retry in a fresh
        // unit of work so a doomed SQL Server transaction is never continued.
        Exception? last = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                await PersistAsync(userId, hash, now, expires, cancellationToken);
                return (raw, expires);
            }
            catch (Exception ex) when (
                attempt < 2
                && (ex is ConcurrencyConflictException || _persistence.IsUniqueConstraintViolation(ex)))
            {
                last = ex;
            }
        }

        throw last ?? new InvalidOperationException("MFA login challenge could not be issued.");
    }

    private async Task PersistAsync(
        Guid userId,
        string hash,
        DateTime now,
        DateTime expires,
        CancellationToken cancellationToken)
    {
        await _unitOfWork.ExecuteInTransactionAsync(async ct =>
        {
            var existing = await _challenges.GetByUserIdAsync(userId, ct);
            if (existing is null)
            {
                await _challenges.AddAsync(
                    MfaLoginChallenge.Create(userId, hash, expires, createdAtUtc: now),
                    ct);
                await _unitOfWork.SaveChangesAsync(ct);
                return;
            }

            existing.Replace(hash, now, expires);
            await _unitOfWork.SaveChangesAsync(ct);
        }, cancellationToken);
    }
}
