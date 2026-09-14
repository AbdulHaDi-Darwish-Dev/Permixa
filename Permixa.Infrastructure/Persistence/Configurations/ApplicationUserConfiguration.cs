using Permixa.Infrastructure.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Permixa.Infrastructure.Persistence.Configurations;

public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.Property(u => u.AuthorizationVersion)
            .IsRequired()
            .HasDefaultValue(ApplicationUser.InitialAuthorizationVersion)
            .IsConcurrencyToken();

        builder.Property(u => u.IsDisabled)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(u => u.PendingEmail)
            .HasMaxLength(256);
    }
}
