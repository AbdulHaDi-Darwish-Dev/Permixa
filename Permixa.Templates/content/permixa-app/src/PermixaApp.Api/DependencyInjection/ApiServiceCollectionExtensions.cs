using Microsoft.Extensions.DependencyInjection;

namespace PermixaApp.Api.DependencyInjection;

/// <summary>Pure Api-host registrations (OpenAPI / Swagger). Not business or infrastructure.</summary>
public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddApiServices(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen();
        return services;
    }
}
