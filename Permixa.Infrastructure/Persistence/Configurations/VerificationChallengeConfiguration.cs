using Permixa.Domain.Verification;
using Permixa.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Permixa.Infrastructure.Persistence.Configurations;

public sealed class VerificationChallengeConfiguration : IEntityTypeConfiguration<VerificationChallenge>
{
    public void Configure(EntityTypeBuilder<VerificationChallenge> builder)
    {
        builder.ToTable("VerificationChallenges");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Purpose).IsRequired().HasConversion<int>();
        builder.Property(c => c.Method).IsRequired().HasConversion<int>();
        builder.Property(c => c.Channel).IsRequired().HasConversion<int>();

        builder.Property(c => c.Destination)
            .IsRequired()
            .HasMaxLength(320);

        builder.Property(c => c.CreatedAtUtc).IsRequired();
        builder.Property(c => c.ExpiresAtUtc).IsRequired();
        builder.Property(c => c.FailedAttempts).IsRequired();

        // One OPEN challenge per (UserId, Purpose, Destination).
        // OPEN = ConsumedAtUtc IS NULL AND InvalidatedAtUtc IS NULL (includes expired unresolved rows).
        builder.HasIndex(c => new { c.UserId, c.Purpose, c.Destination })
            .IsUnique()
            .HasFilter("[ConsumedAtUtc] IS NULL AND [InvalidatedAtUtc] IS NULL")
            .HasDatabaseName("IX_VerificationChallenges_Open_UserId_Purpose_Destination");

        builder.HasIndex(c => c.ExpiresAtUtc);

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
