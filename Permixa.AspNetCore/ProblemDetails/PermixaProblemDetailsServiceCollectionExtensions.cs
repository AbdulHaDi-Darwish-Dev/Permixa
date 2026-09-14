using Permixa.AspNetCore.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.AspNetCore.ProblemDetails;

public static class PermixaProblemDetailsServiceCollectionExtensions
{
    /// <summary>
    /// Registers ProblemDetails and <see cref="PermixaExceptionHandler"/>.
    /// Host middleware order (no <c>UsePermixa</c> mega-helper):
    /// <c>app.UseExceptionHandler();</c> then routing as needed, then
    /// <c>app.UseAuthentication();</c> <c>app.UseAuthorization();</c>.
    /// Do not install Permixa-wide status-code pages for host 404/405 in Phase 8.
    /// </summary>
    public static IServiceCollection AddPermixaProblemDetails(this IServiceCollection services)
    {
        services.AddProblemDetails();
        services.AddExceptionHandler<PermixaExceptionHandler>();
        return services;
    }
}
