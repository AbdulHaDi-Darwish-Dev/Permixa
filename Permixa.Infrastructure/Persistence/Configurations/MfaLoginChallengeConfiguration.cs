using Permixa.Domain.Authentication;
using Permixa.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Permixa.Infrastructure.Persistence.Configurations;

public sealed class MfaLoginChallengeConfiguration : IEntityTypeConfiguration<MfaLoginChallenge>
{
    public void Configure(EntityTypeBuilder<MfaLoginChallenge> builder)
    {
        builder.ToTable("MfaLoginChallenges");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.UserId).IsRequired();
        builder.HasIndex(c => c.UserId)
            .IsUnique()
            .HasFilter(null);

        builder.Property(c => c.ProofHash)
            .IsRequired()
            .HasMaxLength(64)
            .IsConcurrencyToken();

        builder.HasIndex(c => c.ProofHash)
            .IsUnique()
            .HasFilter(null);

        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.ExpiresAtUtc).IsRequired();
        builder.Property(c => c.AttemptCount).IsRequired();

        builder.Property(c => c.ConsumedAtUtc)
            .IsConcurrencyToken();

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
