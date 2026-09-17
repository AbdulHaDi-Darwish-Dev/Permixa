using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PermixaApp.Application.Abstractions;
using PermixaApp.Application.Reference.SampleNotes;
using PermixaApp.Infrastructure.Persistence;
using PermixaApp.Infrastructure.Persistence.Repositories;

namespace PermixaApp.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddAppInfrastructure(
        this IServiceCollection services,
        string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("A SQL Server connection string is required for AppDbContext.");

        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable(AppDbContext.MigrationsHistoryTable)));

        services.AddScoped<ISampleNoteRepository, SampleNoteRepository>();
        services.AddScoped<IAppUnitOfWork, AppUnitOfWork>();
        services.AddSingleton<IAppClock, SystemAppClock>();

        services.AddScoped<CreateSampleNoteUseCase>();
        services.AddScoped<GetSampleNoteByIdUseCase>();
        services.AddScoped<ListSampleNotesUseCase>();

        return services;
    }
}
