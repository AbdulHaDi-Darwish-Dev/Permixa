using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace Permixa.AspNetCore.ProblemDetails;

internal static class TraceIds
{
    public static string Resolve(HttpContext httpContext) =>
        Activity.Current?.Id ?? httpContext.TraceIdentifier;
}
