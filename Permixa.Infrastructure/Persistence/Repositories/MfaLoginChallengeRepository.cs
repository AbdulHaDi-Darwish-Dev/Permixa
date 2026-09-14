using Permixa.Application.Authentication.Abstractions;
using Permixa.Domain.Authentication;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence.Repositories;

public sealed class MfaLoginChallengeRepository : IMfaLoginChallengeRepository
{
    private readonly ApplicationDbContext _db;

    public MfaLoginChallengeRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<MfaLoginChallenge?> GetByUserIdAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        _db.MfaLoginChallenges.FirstOrDefaultAsync(c => c.UserId == userId, cancellationToken);

    public Task<MfaLoginChallenge?> GetByProofHashAsync(
        string proofHash,
        CancellationToken cancellationToken = default) =>
        _db.MfaLoginChallenges.FirstOrDefaultAsync(c => c.ProofHash == proofHash, cancellationToken);

    public Task AddAsync(
        MfaLoginChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        _db.MfaLoginChallenges.Add(challenge);
        return Task.CompletedTask;
    }

    public async Task<int> IncrementAttemptsAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default)
    {
        var updated = await _db.MfaLoginChallenges
            .Where(c => c.Id == challengeId && c.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(c => c.AttemptCount, c => c.AttemptCount + 1),
                cancellationToken);

        if (updated == 0)
            return int.MaxValue;

        return await _db.MfaLoginChallenges.AsNoTracking()
            .Where(c => c.Id == challengeId)
            .Select(c => c.AttemptCount)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public Task<int> ConsumeUnconsumedAsync(
        Guid challengeId,
        DateTime consumedAtUtc,
        CancellationToken cancellationToken = default) =>
        _db.MfaLoginChallenges
            .Where(c => c.Id == challengeId && c.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(c => c.ConsumedAtUtc, consumedAtUtc),
                cancellationToken);
}
