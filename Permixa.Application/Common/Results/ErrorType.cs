namespace Permixa.Application.Common.Results;

/// <summary>
/// Classification of expected application failures.
/// Mapped to HTTP later by the ASP.NET Core layer — not here.
/// </summary>
public enum ErrorType
{
    Failure = 0,
    Validation = 1,
    NotFound = 2,
    Conflict = 3,
    Unauthorized = 4,
    Forbidden = 5
}
