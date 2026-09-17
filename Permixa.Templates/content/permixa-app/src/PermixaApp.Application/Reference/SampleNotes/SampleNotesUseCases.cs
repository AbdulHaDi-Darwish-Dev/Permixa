using PermixaApp.Application.Abstractions;
using PermixaApp.Domain.Reference.SampleNotes;

namespace PermixaApp.Application.Reference.SampleNotes;

/// <summary>REFERENCE / SAFE TO DELETE</summary>
public sealed class CreateSampleNoteRequest
{
    public required string Title { get; init; }

    public string Body { get; init; } = string.Empty;
}

/// <summary>REFERENCE / SAFE TO DELETE</summary>
public sealed class SampleNoteDto
{
    public required Guid Id { get; init; }

    public required string Title { get; init; }

    public required string Body { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public static SampleNoteDto From(SampleNote note) =>
        new()
        {
            Id = note.Id,
            Title = note.Title,
            Body = note.Body,
            CreatedAtUtc = note.CreatedAtUtc
        };
}

/// <summary>REFERENCE / SAFE TO DELETE</summary>
public sealed class CreateSampleNoteUseCase
{
    private readonly ISampleNoteRepository _repository;
    private readonly IAppUnitOfWork _unitOfWork;
    private readonly IAppClock _clock;

    public CreateSampleNoteUseCase(
        ISampleNoteRepository repository,
        IAppUnitOfWork unitOfWork,
        IAppClock clock)
    {
        _repository = repository;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<SampleNoteDto> ExecuteAsync(
        CreateSampleNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var note = SampleNote.Create(request.Title, request.Body, _clock.UtcNow);
        await _repository.AddAsync(note, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return SampleNoteDto.From(note);
    }
}

/// <summary>REFERENCE / SAFE TO DELETE</summary>
public sealed class GetSampleNoteByIdUseCase
{
    private readonly ISampleNoteRepository _repository;

    public GetSampleNoteByIdUseCase(ISampleNoteRepository repository)
    {
        _repository = repository;
    }

    public async Task<SampleNoteDto?> ExecuteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var note = await _repository.GetByIdAsync(id, cancellationToken);
        return note is null ? null : SampleNoteDto.From(note);
    }
}

/// <summary>REFERENCE / SAFE TO DELETE</summary>
public sealed class ListSampleNotesUseCase
{
    private readonly ISampleNoteRepository _repository;

    public ListSampleNotesUseCase(ISampleNoteRepository repository)
    {
        _repository = repository;
    }

    public async Task<IReadOnlyList<SampleNoteDto>> ExecuteAsync(CancellationToken cancellationToken = default)
    {
        var notes = await _repository.ListAsync(cancellationToken);
        return notes.Select(SampleNoteDto.From).ToList();
    }
}
