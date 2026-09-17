using Permixa.Application.Authorization.EffectivePermissions;
using Permixa.AspNetCore.Http;
using Permixa.AspNetCore.Security;

namespace PermixaApp.Api.Endpoints;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoint(this IEndpointRouteBuilder app)
    {
        app.MapGet("/me", async (
            ICurrentUser currentUser,
            GetMyEffectivePermissionsUseCase permissions,
            HttpContext http) =>
        {
            if (!currentUser.IsAuthenticated || currentUser.UserId is null)
                return Results.Unauthorized();

            var result = await permissions.ExecuteAsync(
                new GetMyEffectivePermissionsQuery(currentUser.UserId.Value));

            return result.ToHttpResult(http, value => Results.Json(new
            {
                userId = currentUser.UserId,
                permissions = value.Permissions
            }));
        }).RequireAuthorization();

        return app;
    }
}
