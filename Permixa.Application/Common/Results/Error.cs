namespace Permixa.Application.Common.Results;

/// <summary>
/// Structured expected-failure detail for the Result pattern.
/// </summary>
public sealed class Error : IEquatable<Error>
{
    public Error(string code, string description, ErrorType type)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new ArgumentException("Error code cannot be empty.", nameof(code));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException("Error description cannot be empty.", nameof(description));

        Code = code.Trim();
        Description = description.Trim();
        Type = type;
    }

    public string Code { get; }

    public string Description { get; }

    public ErrorType Type { get; }

    public static Error Failure(string code, string description) =>
        new(code, description, ErrorType.Failure);

    public static Error Validation(string code, string description) =>
        new(code, description, ErrorType.Validation);

    public static Error NotFound(string code, string description) =>
        new(code, description, ErrorType.NotFound);

    public static Error Conflict(string code, string description) =>
        new(code, description, ErrorType.Conflict);

    public static Error Unauthorized(string code, string description) =>
        new(code, description, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string description) =>
        new(code, description, ErrorType.Forbidden);

    public bool Equals(Error? other)
    {
        if (other is null)
            return false;

        return Code == other.Code
               && Description == other.Description
               && Type == other.Type;
    }

    public override bool Equals(object? obj) => obj is Error other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Code, Description, Type);

    public static bool operator ==(Error? left, Error? right) =>
        left is null ? right is null : left.Equals(right);

    public static bool operator !=(Error? left, Error? right) => !(left == right);

    public override string ToString() => $"{Code}: {Description} ({Type})";
}
