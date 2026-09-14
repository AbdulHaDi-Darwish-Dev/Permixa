using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.AspNetCore.Authorization;

public static class PermixaPermissionAuthorizationServiceCollectionExtensions
{
    /// <summary>
    /// Registers dynamic <c>Permixa.Permission:*</c> policies and a scoped permission handler.
    /// Requires host <c>UseAuthentication</c>/<c>UseAuthorization</c> and an
    /// <c>IEffectivePermissionService</c> registration from Application/Infrastructure.
    /// </summary>
    public static IServiceCollection AddPermixaPermissionAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationPolicyProvider, PermixaAuthorizationPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionAuthorizationHandler>();
        return services;
    }
}
