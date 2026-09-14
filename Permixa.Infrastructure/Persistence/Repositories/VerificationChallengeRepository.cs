using Permixa.Application.Verification.Abstractions;
using Permixa.Domain.Verification;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence.Repositories;

public sealed class VerificationChallengeRepository : IVerificationChallengeRepository
{
    private readonly ApplicationDbContext _db;

    public VerificationChallengeRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<VerificationChallenge?> GetByIdAsync(
        Guid challengeId,
        CancellationToken cancellationToken = default) =>
        _db.VerificationChallenges
            .FirstOrDefaultAsync(c => c.Id == challengeId, cancellationToken);

    public async Task<VerificationChallenge?> GetActiveAsync(
        Guid userId,
        VerificationPurpose purpose,
        string destination,
        CancellationToken cancellationToken = default)
    {
        var utcNow = DateTime.UtcNow;

        // SQL filters immutable lifecycle columns; Domain IsActive remains authoritative for behavior.
        var candidates = await _db.VerificationChallenges
            .Where(c =>
                c.UserId == userId
                && c.Purpose == purpose
                && c.Destination == destination
                && c.ConsumedAtUtc == null
                && c.InvalidatedAtUtc == null
                && c.ExpiresAtUtc > utcNow)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return candidates.FirstOrDefault(c => c.IsActive(utcNow));
    }

    public async Task<IReadOnlyList<VerificationChallenge>> GetOpenAsync(
        Guid userId,
        VerificationPurpose purpose,
        string destination,
        CancellationToken cancellationToken = default)
    {
        return await _db.VerificationChallenges
            .Where(c =>
                c.UserId == userId
                && c.Purpose == purpose
                && c.Destination == destination
                && c.ConsumedAtUtc == null
                && c.InvalidatedAtUtc == null)
            .OrderByDescending(c => c.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public Task AddAsync(
        VerificationChallenge challenge,
        CancellationToken cancellationToken = default)
    {
        _db.VerificationChallenges.Add(challenge);
        return Task.CompletedTask;
    }

    public Task<int> InvalidateOpenForPurposeAsync(
        Guid userId,
        VerificationPurpose purpose,
        DateTime invalidatedAtUtc,
        CancellationToken cancellationToken = default) =>
        _db.VerificationChallenges
            .Where(c =>
                c.UserId == userId
                && c.Purpose == purpose
                && c.ConsumedAtUtc == null
                && c.InvalidatedAtUtc == null)
            .ExecuteUpdateAsync(
                updates => updates.SetProperty(c => c.InvalidatedAtUtc, invalidatedAtUtc),
                cancellationToken);
}
