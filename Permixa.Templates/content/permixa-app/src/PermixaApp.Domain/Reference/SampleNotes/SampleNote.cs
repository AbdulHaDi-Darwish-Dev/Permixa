namespace PermixaApp.Domain.Reference.SampleNotes;

/// <summary>
/// REFERENCE / SAFE TO DELETE — tiny sample entity for the vertical-slice demo.
/// </summary>
public sealed class SampleNote
{
    public Guid Id { get; private set; }

    public string Title { get; private set; } = string.Empty;

    public string Body { get; private set; } = string.Empty;

    public DateTime CreatedAtUtc { get; private set; }

    private SampleNote()
    {
    }

    public static SampleNote Create(string title, string body, DateTime createdAtUtc)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new ArgumentException("Title is required.", nameof(title));

        if (title.Trim().Length > 200)
            throw new ArgumentException("Title must be 200 characters or fewer.", nameof(title));

        body ??= string.Empty;
        if (body.Length > 4000)
            throw new ArgumentException("Body must be 4000 characters or fewer.", nameof(body));

        return new SampleNote
        {
            Id = Guid.NewGuid(),
            Title = title.Trim(),
            Body = body.Trim(),
            CreatedAtUtc = createdAtUtc
        };
    }
}
