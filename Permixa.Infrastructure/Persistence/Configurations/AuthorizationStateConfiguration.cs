using Permixa.Domain.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Permixa.Infrastructure.Persistence.Configurations;

public sealed class AuthorizationStateConfiguration : IEntityTypeConfiguration<AuthorizationState>
{
    public void Configure(EntityTypeBuilder<AuthorizationState> builder)
    {
        builder.ToTable("AuthorizationStates");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.RbacVersion)
            .IsRequired()
            .IsConcurrencyToken();
    }
}
