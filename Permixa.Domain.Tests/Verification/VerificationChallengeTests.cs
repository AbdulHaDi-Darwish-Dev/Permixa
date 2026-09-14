using Permixa.Domain.Common;
using Permixa.Domain.Verification;

namespace Permixa.Domain.Tests.Verification;

public sealed class VerificationChallengeTests
{
    private static readonly DateTime CreatedAt = new(2026, 4, 1, 10, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiresAt = CreatedAt.AddMinutes(10);

    private static VerificationChallenge CreateChallenge(
        DateTime? createdAtUtc = null,
        DateTime? expiresAtUtc = null)
    {
        return VerificationChallenge.Create(
            Guid.NewGuid(),
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "user@example.com",
            expiresAtUtc ?? ExpiresAt,
            createdAtUtc: createdAtUtc ?? CreatedAt);
    }

    [Fact]
    public void NewChallenge_IsActiveBeforeExpiration()
    {
        var challenge = CreateChallenge();

        Assert.True(challenge.IsActive(CreatedAt.AddMinutes(1)));
        Assert.False(challenge.IsExpired(CreatedAt.AddMinutes(1)));
        Assert.False(challenge.IsConsumed);
        Assert.False(challenge.IsInvalidated);
        Assert.Equal(0, challenge.FailedAttempts);
    }

    [Fact]
    public void ExpiredChallenge_IsNotActive()
    {
        var challenge = CreateChallenge();

        Assert.True(challenge.IsExpired(ExpiresAt));
        Assert.False(challenge.IsActive(ExpiresAt));
    }

    [Fact]
    public void Consume_SetsConsumedAtUtc_AndCannotBeReused()
    {
        var challenge = CreateChallenge();
        var consumedAt = CreatedAt.AddMinutes(2);

        challenge.Consume(consumedAt);

        Assert.True(challenge.IsConsumed);
        Assert.Equal(consumedAt, challenge.ConsumedAtUtc);
        Assert.False(challenge.IsActive(CreatedAt.AddMinutes(3)));
        Assert.Throws<DomainException>(() => challenge.Consume(CreatedAt.AddMinutes(4)));
    }

    [Fact]
    public void Invalidate_MakesChallengeInactive_AndCannotConsume()
    {
        var challenge = CreateChallenge();
        var invalidatedAt = CreatedAt.AddMinutes(1);

        challenge.Invalidate(invalidatedAt);

        Assert.True(challenge.IsInvalidated);
        Assert.Equal(invalidatedAt, challenge.InvalidatedAtUtc);
        Assert.False(challenge.IsActive(CreatedAt.AddMinutes(2)));
        Assert.Throws<DomainException>(() => challenge.Consume());
    }

    [Fact]
    public void RegisterFailedAttempt_IncrementsCorrectly()
    {
        var challenge = CreateChallenge();

        challenge.RegisterFailedAttempt();
        challenge.RegisterFailedAttempt();

        Assert.Equal(2, challenge.FailedAttempts);
    }

    [Fact]
    public void RegisterFailedAttempt_OnConsumedChallenge_Throws()
    {
        var challenge = CreateChallenge();
        challenge.Consume(CreatedAt.AddMinutes(1));

        Assert.Throws<DomainException>(() => challenge.RegisterFailedAttempt());
    }

    [Fact]
    public void UrlTokenAndSms_AreRepresentedIndependently()
    {
        var challenge = VerificationChallenge.Create(
            Guid.NewGuid(),
            VerificationPurpose.PhoneConfirmation,
            VerificationMethod.UrlToken,
            VerificationChannel.Sms,
            "+15551234567",
            ExpiresAt,
            createdAtUtc: CreatedAt);

        Assert.Equal(VerificationMethod.UrlToken, challenge.Method);
        Assert.Equal(VerificationChannel.Sms, challenge.Channel);
        Assert.True(challenge.IsActive(CreatedAt.AddSeconds(1)));
    }

    [Fact]
    public void EmailChange_IsADistinctPurpose()
    {
        var challenge = VerificationChallenge.Create(
            Guid.NewGuid(),
            VerificationPurpose.EmailChange,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            "new@example.com",
            ExpiresAt,
            createdAtUtc: CreatedAt);

        Assert.Equal(VerificationPurpose.EmailChange, challenge.Purpose);
        Assert.Equal("new@example.com", challenge.Destination);
        Assert.True(challenge.IsActive(CreatedAt.AddSeconds(1)));
    }
}
