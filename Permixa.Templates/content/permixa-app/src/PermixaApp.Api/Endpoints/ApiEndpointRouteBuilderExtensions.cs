using PermixaApp.Api.Endpoints;

namespace PermixaApp.Api.Endpoints;

/// <summary>Aggregates host endpoint mapping only — no business logic.</summary>
public static class ApiEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapApiEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/health", () => Results.Ok(new { status = "ok" }));
        endpoints.MapAuthEndpoints();
#if (resend)
        endpoints.MapEmailConfirmationEndpoints();
#endif
        endpoints.MapMeEndpoint();
        endpoints.MapSampleEndpoints();
        return endpoints;
    }
}
