using Microsoft.EntityFrameworkCore;
using PermixaApp.Domain.Reference.SampleNotes;

namespace PermixaApp.Infrastructure.Persistence;

public sealed class AppDbContext : DbContext
{
    public const string MigrationsHistoryTable = "__AppMigrationsHistory";

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<SampleNote> SampleNotes => Set<SampleNote>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
