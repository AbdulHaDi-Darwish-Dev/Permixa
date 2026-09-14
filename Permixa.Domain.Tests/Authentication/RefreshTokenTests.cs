using Permixa.Domain.Authentication;
using Permixa.Domain.Common;

namespace Permixa.Domain.Tests.Authentication;

public sealed class RefreshTokenTests
{
    private static readonly DateTime CreatedAt = new(2026, 3, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime ExpiresAt = CreatedAt.AddDays(7);
    private static readonly Guid FamilyId = Guid.Parse("aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");

    [Fact]
    public void ActiveToken_IsActiveBeforeExpiration()
    {
        var token = RefreshToken.Create(
            Guid.NewGuid(),
            "hash-value",
            ExpiresAt,
            FamilyId,
            createdAtUtc: CreatedAt);

        Assert.True(token.IsActive(CreatedAt.AddHours(1)));
        Assert.False(token.IsExpired(CreatedAt.AddHours(1)));
        Assert.False(token.IsRevoked);
        Assert.Equal(FamilyId, token.FamilyId);
        Assert.False(token.WasReplaced);
    }

    [Fact]
    public void ExpiredToken_IsNotActive()
    {
        var token = RefreshToken.Create(
            Guid.NewGuid(),
            "hash-value",
            ExpiresAt,
            FamilyId,
            createdAtUtc: CreatedAt);

        Assert.True(token.IsExpired(ExpiresAt));
        Assert.False(token.IsActive(ExpiresAt));
        Assert.False(token.IsRevoked);
    }

    [Fact]
    public void RevokedToken_IsNotActive()
    {
        var token = RefreshToken.Create(
            Guid.NewGuid(),
            "hash-value",
            ExpiresAt,
            FamilyId,
            createdAtUtc: CreatedAt);

        var revokedAt = CreatedAt.AddHours(2);
        var replacementId = Guid.NewGuid();

        token.Revoke(revokedAt, replacementId);

        Assert.True(token.IsRevoked);
        Assert.Equal(revokedAt, token.RevokedAtUtc);
        Assert.Equal(replacementId, token.ReplacedByTokenId);
        Assert.True(token.WasReplaced);
        Assert.False(token.IsActive(CreatedAt.AddHours(3)));
    }

    [Fact]
    public void Revoke_WithoutReplacement_IsNotWasReplaced()
    {
        var token = RefreshToken.Create(
            Guid.NewGuid(),
            "hash-value",
            ExpiresAt,
            FamilyId,
            createdAtUtc: CreatedAt);

        token.Revoke(CreatedAt.AddMinutes(1));

        Assert.True(token.IsRevoked);
        Assert.False(token.WasReplaced);
    }

    [Fact]
    public void Revoke_WhenAlreadyRevoked_Throws()
    {
        var token = RefreshToken.Create(
            Guid.NewGuid(),
            "hash-value",
            ExpiresAt,
            FamilyId,
            createdAtUtc: CreatedAt);

        token.Revoke(CreatedAt.AddMinutes(1));

        Assert.Throws<DomainException>(() => token.Revoke(CreatedAt.AddMinutes(2)));
    }

    [Fact]
    public void Create_WithEmptyFamilyId_Throws()
    {
        Assert.Throws<ArgumentException>(() =>
            RefreshToken.Create(Guid.NewGuid(), "hash", ExpiresAt, Guid.Empty, createdAtUtc: CreatedAt));
    }
}
