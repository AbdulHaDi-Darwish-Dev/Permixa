using Permixa.Application.Common.Results;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Permixa.AspNetCore.Http;

/// <summary>
/// Minimal API helpers for mapping Application Result failures to ProblemDetails.
/// Success status remains caller-controlled.
/// </summary>
public static class PermixaResultMinimalApiExtensions
{
    public static IResult ToProblemDetails(this Error error, HttpContext httpContext) =>
        Results.Json(
            Permixa.AspNetCore.ProblemDetails.PermixaProblemDetailsMapper.FromError(error, httpContext),
            statusCode: Permixa.AspNetCore.ProblemDetails.PermixaProblemDetailsMapper.MapStatusCode(error.Type),
            contentType: "application/problem+json");

    public static IResult ToHttpResult<T>(
        this Result<T> result,
        HttpContext httpContext,
        Func<T, IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
            return onSuccess(result.Value);

        return result.Error!.ToProblemDetails(httpContext);
    }

    public static IResult ToHttpResult(
        this Result result,
        HttpContext httpContext,
        Func<IResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
            return onSuccess();

        return result.Error!.ToProblemDetails(httpContext);
    }
}

/// <summary>
/// MVC helpers for mapping Application Result failures to ActionResult ProblemDetails.
/// </summary>
public static class PermixaResultMvcExtensions
{
    public static ActionResult ToActionResult(this Error error, HttpContext httpContext)
    {
        var problem = Permixa.AspNetCore.ProblemDetails.PermixaProblemDetailsMapper.FromError(error, httpContext);
        return new ObjectResult(problem)
        {
            StatusCode = problem.Status,
            ContentTypes = { "application/problem+json" }
        };
    }

    public static ActionResult ToActionResult<T>(
        this Result<T> result,
        HttpContext httpContext,
        Func<T, ActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
            return onSuccess(result.Value);

        return result.Error!.ToActionResult(httpContext);
    }

    public static ActionResult ToActionResult(
        this Result result,
        HttpContext httpContext,
        Func<ActionResult> onSuccess)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(onSuccess);

        if (result.IsSuccess)
            return onSuccess();

        return result.Error!.ToActionResult(httpContext);
    }
}
