using Microsoft.EntityFrameworkCore;
using PermixaApp.Application.Abstractions;
using PermixaApp.Domain.Reference.SampleNotes;
using PermixaApp.Infrastructure.Persistence;

namespace PermixaApp.Infrastructure.Persistence.Repositories;

public sealed class SampleNoteRepository : ISampleNoteRepository
{
    private readonly AppDbContext _db;

    public SampleNoteRepository(AppDbContext db)
    {
        _db = db;
    }

    public Task AddAsync(SampleNote note, CancellationToken cancellationToken = default)
    {
        _db.SampleNotes.Add(note);
        return Task.CompletedTask;
    }

    public Task<SampleNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default) =>
        _db.SampleNotes.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public async Task<IReadOnlyList<SampleNote>> ListAsync(CancellationToken cancellationToken = default) =>
        await _db.SampleNotes.AsNoTracking()
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
}

public sealed class AppUnitOfWork : IAppUnitOfWork
{
    private readonly AppDbContext _db;

    public AppUnitOfWork(AppDbContext db)
    {
        _db = db;
    }

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) =>
        _db.SaveChangesAsync(cancellationToken);
}

public sealed class SystemAppClock : IAppClock
{
    public DateTime UtcNow => DateTime.UtcNow;
}
