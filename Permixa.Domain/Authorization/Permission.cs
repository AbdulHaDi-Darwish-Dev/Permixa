using Permixa.Domain.Common;

namespace Permixa.Domain.Authorization;

/// <summary>
/// Named permission following the Resource.Action convention (e.g. Users.Read).
/// Consuming applications define their own permission catalog.
/// </summary>
public sealed class Permission
{
    public const int MaxNameLength = 256;
    public const int MaxDescriptionLength = 512;

    private Permission(
        Guid id,
        string name,
        string? description,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        Id = id;
        Name = name;
        Description = description;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }

    public string Name { get; private set; }

    public string? Description { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static Permission Create(
        string name,
        string? description = null,
        Guid? id = null,
        DateTime? createdAtUtc = null)
    {
        ValidateName(name);
        ValidateDescription(description);

        var now = createdAtUtc ?? DateTime.UtcNow;
        var trimmedDescription = string.IsNullOrWhiteSpace(description)
            ? null
            : description.Trim();

        return new Permission(id ?? Guid.NewGuid(), name.Trim(), trimmedDescription, now, now);
    }

    public static Permission Reconstitute(
        Guid id,
        string name,
        string? description,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Permission id cannot be empty.", nameof(id));

        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new Permission(id, name, description, createdAtUtc, updatedAtUtc);
    }

    public void UpdateDescription(string? description, DateTime? updatedAtUtc = null)
    {
        ValidateDescription(description);
        Description = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow;
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new DomainException("Permission name cannot be empty.");

        if (name.Trim().Length > MaxNameLength)
            throw new DomainException($"Permission name cannot exceed {MaxNameLength} characters.");
    }

    private static void ValidateDescription(string? description)
    {
        if (description is not null && description.Length > MaxDescriptionLength)
            throw new DomainException($"Permission description cannot exceed {MaxDescriptionLength} characters.");
    }
}
