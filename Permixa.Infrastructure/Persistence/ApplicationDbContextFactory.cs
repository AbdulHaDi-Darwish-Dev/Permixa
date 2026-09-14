using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Permixa.Infrastructure.Persistence;

/// <summary>
/// Design-time factory for EF migrations (SQL Server).
/// Requires environment variable PERMIXA_CONNECTION_STRING.
/// </summary>
public sealed class ApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public const string ConnectionStringEnvironmentVariable = "PERMIXA_CONNECTION_STRING";

    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionStringEnvironmentVariable);

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"EF Core design-time operations require the environment variable '{ConnectionStringEnvironmentVariable}' " +
                "to be set to a SQL Server connection string. " +
                "Example development database name: PermixaDb. " +
                "Do not rely on embedded credentials or LocalDB defaults in this factory.");
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new ApplicationDbContext(options);
    }
}
