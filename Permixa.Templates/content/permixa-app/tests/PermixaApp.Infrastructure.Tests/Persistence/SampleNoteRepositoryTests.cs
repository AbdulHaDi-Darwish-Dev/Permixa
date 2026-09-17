using Microsoft.EntityFrameworkCore;
using PermixaApp.Domain.Reference.SampleNotes;
using PermixaApp.Infrastructure.Persistence;
using PermixaApp.Infrastructure.Persistence.Repositories;
using Testcontainers.MsSql;
using Xunit;

namespace PermixaApp.Infrastructure.Tests.Persistence;

/// <summary>REFERENCE / SAFE TO DELETE — AppDbContext round-trip.</summary>
public sealed class SampleNoteRepositoryTests : IAsyncLifetime
{
    private readonly MsSqlContainer _sql = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public Task InitializeAsync() => _sql.StartAsync();

    public Task DisposeAsync() => _sql.DisposeAsync().AsTask();

    [Fact]
    public async Task AddAndGetById_RoundTrips()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(_sql.GetConnectionString(), sql =>
                sql.MigrationsHistoryTable(AppDbContext.MigrationsHistoryTable))
            .Options;

        await using (var db = new AppDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            var repo = new SampleNoteRepository(db);
            var uow = new AppUnitOfWork(db);
            var note = SampleNote.Create("Title", "Body", DateTime.UtcNow);
            await repo.AddAsync(note);
            await uow.SaveChangesAsync();

            var loaded = await repo.GetByIdAsync(note.Id);
            Assert.NotNull(loaded);
            Assert.Equal("Title", loaded!.Title);
        }
    }
}
