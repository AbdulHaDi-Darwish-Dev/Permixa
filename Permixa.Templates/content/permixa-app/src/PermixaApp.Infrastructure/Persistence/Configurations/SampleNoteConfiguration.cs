using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PermixaApp.Domain.Reference.SampleNotes;

namespace PermixaApp.Infrastructure.Persistence.Configurations;

/// <summary>REFERENCE / SAFE TO DELETE</summary>
public sealed class SampleNoteConfiguration : IEntityTypeConfiguration<SampleNote>
{
    public void Configure(EntityTypeBuilder<SampleNote> builder)
    {
        builder.ToTable("SampleNotes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasMaxLength(4000).IsRequired();
        builder.Property(x => x.CreatedAtUtc).IsRequired();
    }
}
