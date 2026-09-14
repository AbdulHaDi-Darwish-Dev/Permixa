namespace Permixa.Application.Common.Results;

/// <summary>
/// Permixaal machine-readable error codes.
/// Feature-specific codes will be added alongside Use Cases.
/// </summary>
public static class ApplicationErrors
{
    public static readonly Error Unexpected =
        Error.Failure("Application.Unexpected", "An unexpected application error occurred.");

    public static readonly Error ValidationFailed =
        Error.Validation("Application.ValidationFailed", "One or more validation errors occurred.");

    public static readonly Error Unauthorized =
        Error.Unauthorized("Application.Unauthorized", "The current operation is not authorized.");

    public static readonly Error Forbidden =
        Error.Forbidden("Application.Forbidden", "The current operation is forbidden.");
}
