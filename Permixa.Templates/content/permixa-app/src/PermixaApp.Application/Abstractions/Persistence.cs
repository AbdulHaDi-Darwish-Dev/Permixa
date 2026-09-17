using PermixaApp.Domain.Reference.SampleNotes;

namespace PermixaApp.Application.Abstractions;

public interface ISampleNoteRepository
{
    Task AddAsync(SampleNote note, CancellationToken cancellationToken = default);

    Task<SampleNote?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SampleNote>> ListAsync(CancellationToken cancellationToken = default);
}

public interface IAppUnitOfWork
{
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IAppClock
{
    DateTime UtcNow { get; }
}
