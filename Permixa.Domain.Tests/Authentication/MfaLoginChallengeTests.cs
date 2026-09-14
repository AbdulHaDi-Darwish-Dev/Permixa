using Permixa.Domain.Authentication;
using Permixa.Domain.Common;

namespace Permixa.Domain.Tests.Authentication;

public sealed class MfaLoginChallengeTests
{
    private static readonly DateTime CreatedAt = new(2026, 9, 13, 21, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiresAt = CreatedAt.AddMinutes(5);
    private static readonly Guid UserId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    [Fact]
    public void Create_IsActive_UntilExpiration()
    {
        var challenge = MfaLoginChallenge.Create(UserId, "proof-hash", ExpiresAt, createdAtUtc: CreatedAt);

        Assert.True(challenge.IsActive(CreatedAt.AddMinutes(1)));
        Assert.False(challenge.IsExpired(CreatedAt.AddMinutes(1)));
        Assert.False(challenge.IsConsumed);
        Assert.Equal(0, challenge.AttemptCount);
        Assert.Equal("proof-hash", challenge.ProofHash);
    }

    [Fact]
    public void ExpiredChallenge_IsNotActive()
    {
        var challenge = MfaLoginChallenge.Create(UserId, "proof-hash", ExpiresAt, createdAtUtc: CreatedAt);

        Assert.True(challenge.IsExpired(ExpiresAt));
        Assert.False(challenge.IsActive(ExpiresAt));
    }

    [Fact]
    public void Consume_MarksConsumed()
    {
        var challenge = MfaLoginChallenge.Create(UserId, "proof-hash", ExpiresAt, createdAtUtc: CreatedAt);
        var consumedAt = CreatedAt.AddMinutes(1);

        challenge.Consume(consumedAt);

        Assert.True(challenge.IsConsumed);
        Assert.Equal(consumedAt, challenge.ConsumedAtUtc);
        Assert.False(challenge.IsActive(CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Consume_WhenAlreadyConsumed_Throws()
    {
        var challenge = MfaLoginChallenge.Create(UserId, "proof-hash", ExpiresAt, createdAtUtc: CreatedAt);
        challenge.Consume(CreatedAt.AddMinutes(1));

        Assert.Throws<DomainException>(() => challenge.Consume(CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Replace_ResetsProofAttemptsAndConsumed()
    {
        var challenge = MfaLoginChallenge.Create(UserId, "old-hash", ExpiresAt, createdAtUtc: CreatedAt);
        challenge.RegisterFailedAttempt();
        challenge.Consume(CreatedAt.AddMinutes(1));

        var nextCreated = CreatedAt.AddMinutes(2);
        var nextExpires = nextCreated.AddMinutes(5);
        challenge.Replace("new-hash", nextCreated, nextExpires);

        Assert.Equal("new-hash", challenge.ProofHash);
        Assert.Equal(0, challenge.AttemptCount);
        Assert.False(challenge.IsConsumed);
        Assert.True(challenge.IsActive(nextCreated.AddMinutes(1)));
        Assert.Equal(nextCreated, challenge.CreatedAtUtc);
        Assert.Equal(nextExpires, challenge.ExpiresAtUtc);
    }

    [Fact]
    public void RegisterFailedAttempt_OnConsumed_Throws()
    {
        var challenge = MfaLoginChallenge.Create(UserId, "proof-hash", ExpiresAt, createdAtUtc: CreatedAt);
        challenge.Consume(CreatedAt.AddMinutes(1));

        Assert.Throws<DomainException>(() => challenge.RegisterFailedAttempt());
    }

    [Fact]
    public void Create_WithEmptyUserIdOrHash_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            MfaLoginChallenge.Create(Guid.Empty, "hash", ExpiresAt, createdAtUtc: CreatedAt));
        Assert.Throws<DomainException>(() =>
            MfaLoginChallenge.Create(UserId, " ", ExpiresAt, createdAtUtc: CreatedAt));
        Assert.Throws<DomainException>(() =>
            MfaLoginChallenge.Create(UserId, "hash", CreatedAt, createdAtUtc: CreatedAt));
    }
}
