using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PermixaApp.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for consumer business migrations.
/// Set environment variable APP_CONNECTION_STRING (or ConnectionStrings__Default).
/// </summary>
public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public const string ConnectionStringEnvironmentVariable = "APP_CONNECTION_STRING";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable)
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__Default");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Set '{ConnectionStringEnvironmentVariable}' (or ConnectionStrings__Default) " +
                "to a SQL Server connection string for EF design-time operations. " +
                "Do not embed credentials in this factory.");
        }

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsHistoryTable(AppDbContext.MigrationsHistoryTable))
            .Options;

        return new AppDbContext(options);
    }
}
