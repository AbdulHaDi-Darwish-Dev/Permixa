using Permixa.Application.Common;
using Permixa.AspNetCore.ProblemDetails;
using Permixa.Domain.Common;
using Permixa.Infrastructure.Email;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Permixa.AspNetCore.Exceptions;

public sealed class PermixaExceptionHandler : IExceptionHandler
{
    private readonly ILogger<PermixaExceptionHandler> _logger;

    public PermixaExceptionHandler(ILogger<PermixaExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, code, detail, logLevel) = Map(exception);

        _logger.Log(
            logLevel,
            exception,
            "Unhandled exception mapped to HTTP {StatusCode}. Code={Code}",
            status,
            code);

        var problem = PermixaProblemDetailsMapper.Unexpected(
            httpContext,
            status,
            code,
            detail);

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            problem,
            options: null,
            contentType: "application/problem+json",
            cancellationToken);
        return true;
    }

    private static (int Status, string Code, string Detail, LogLevel Level) Map(Exception exception) =>
        exception switch
        {
            ConcurrencyConflictException => (
                StatusCodes.Status409Conflict,
                "Concurrency.Conflict",
                "The resource was modified concurrently. Retry the operation.",
                LogLevel.Warning),

            EmailDeliveryException => (
                StatusCodes.Status503ServiceUnavailable,
                "Email.DeliveryFailed",
                "The email delivery service is temporarily unavailable.",
                LogLevel.Warning),

            DomainException => (
                StatusCodes.Status500InternalServerError,
                PermixaProblemDetailsMapper.InternalErrorCode,
                "An unexpected error occurred.",
                LogLevel.Error),

            _ => (
                StatusCodes.Status500InternalServerError,
                PermixaProblemDetailsMapper.InternalErrorCode,
                "An unexpected error occurred.",
                LogLevel.Error)
        };
}
