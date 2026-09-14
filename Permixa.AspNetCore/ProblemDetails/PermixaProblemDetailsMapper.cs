using Permixa.Application.Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Permixa.AspNetCore.ProblemDetails;

/// <summary>
/// Maps Application <see cref="Error"/> values to HTTP ProblemDetails.
/// </summary>
public static class PermixaProblemDetailsMapper
{
    public const string InternalErrorCode = "InternalError";

    public static int MapStatusCode(ErrorType type) =>
        type switch
        {
            ErrorType.Validation => StatusCodes.Status400BadRequest,
            ErrorType.Failure => StatusCodes.Status400BadRequest,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest
        };

    public static string MapTitle(int statusCode) =>
        statusCode switch
        {
            StatusCodes.Status400BadRequest => "Bad Request",
            StatusCodes.Status401Unauthorized => "Unauthorized",
            StatusCodes.Status403Forbidden => "Forbidden",
            StatusCodes.Status404NotFound => "Not Found",
            StatusCodes.Status409Conflict => "Conflict",
            StatusCodes.Status503ServiceUnavailable => "Service Unavailable",
            _ => "An error occurred"
        };

    public static Microsoft.AspNetCore.Mvc.ProblemDetails FromError(
        Error error,
        HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(error);
        ArgumentNullException.ThrowIfNull(httpContext);

        var status = MapStatusCode(error.Type);
        return new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = status,
            Title = MapTitle(status),
            Detail = error.Description,
            Extensions =
            {
                ["code"] = error.Code,
                ["traceId"] = TraceIds.Resolve(httpContext)
            }
        };
    }

    public static Microsoft.AspNetCore.Mvc.ProblemDetails Unexpected(
        HttpContext httpContext,
        int statusCode,
        string code,
        string detail)
    {
        return new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Status = statusCode,
            Title = MapTitle(statusCode),
            Detail = detail,
            Extensions =
            {
                ["code"] = code,
                ["traceId"] = TraceIds.Resolve(httpContext)
            }
        };
    }
}
