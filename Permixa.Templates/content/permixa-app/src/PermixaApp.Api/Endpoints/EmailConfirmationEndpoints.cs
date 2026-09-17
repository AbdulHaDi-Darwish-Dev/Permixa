using Permixa.Application.Verification.EmailConfirmation;
using Permixa.AspNetCore.Http;
using Permixa.Domain.Verification;

namespace PermixaApp.Api.Endpoints;

public static class EmailConfirmationEndpoints
{
    public static IEndpointRouteBuilder MapEmailConfirmationEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/auth/email-confirmation/request", async (
            RequestEmailConfirmationHttpRequest request,
            RequestEmailConfirmationUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(new RequestEmailConfirmationRequest
            {
                UserId = request.UserId,
                Method = VerificationMethod.Otp
            });
            return result.ToHttpResult(http, value => Results.Json(value));
        });

        app.MapPost("/auth/email-confirmation/confirm", async (
            ConfirmEmailRequest request,
            ConfirmEmailUseCase useCase,
            HttpContext http) =>
        {
            var result = await useCase.ExecuteAsync(request);
            return result.ToHttpResult(http, () => Results.Ok());
        });

        return app;
    }
}

public sealed class RequestEmailConfirmationHttpRequest
{
    public required Guid UserId { get; init; }
}
