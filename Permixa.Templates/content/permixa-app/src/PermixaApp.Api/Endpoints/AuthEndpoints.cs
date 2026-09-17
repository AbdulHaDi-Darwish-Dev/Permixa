using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Logout;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authentication.Register;
using Permixa.AspNetCore.Http;
using Permixa.AspNetCore.RateLimiting;

namespace PermixaApp.Api.Endpoints;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/register", async (
            RegisterRequest request,
            RegisterUserUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, value =>
                Results.Json(value, statusCode: StatusCodes.Status201Created));
        });

        app.MapPost("/auth/login", async (
            LoginRequest request,
            LoginUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, value =>
                value.Authentication is not null
                    ? Results.Json(value.Authentication)
                    : Results.Json(value.Mfa));
        }).RequireRateLimiting("Login");

        app.MapPost("/auth/refresh", async (
            RefreshTokenRequest request,
            RefreshAccessTokenUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, value => Results.Json(value));
        });

        app.MapPost("/auth/logout", async (
            RevokeRefreshTokenRequest request,
            LogoutUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, () => Results.Ok());
        });

        return app;
    }
}
