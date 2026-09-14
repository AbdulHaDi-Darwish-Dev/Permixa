using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace Permixa.IntegrationTests.Support;

public sealed class SqlServerContainerFixture : IAsyncLifetime
{
    public MsSqlContainer? Container { get; private set; }

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            Container = new MsSqlBuilder().Build();
            await Container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = $"SQL Server Testcontainer unavailable ({ex.GetType().Name}): {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (Container is not null)
            await Container.DisposeAsync();
    }

    public string CreateUniqueDatabaseConnectionString()
    {
        if (!IsAvailable || Container is null)
            throw new InvalidOperationException(SkipReason ?? "SQL Server container is not available.");

        var builder = new SqlConnectionStringBuilder(Container.GetConnectionString())
        {
            InitialCatalog = $"PermixaIntegration_{Guid.NewGuid():N}"
        };
        return builder.ConnectionString;
    }
}

public sealed class RedisContainerFixture : IAsyncLifetime
{
    public RedisContainer? Container { get; private set; }

    public bool IsAvailable { get; private set; }

    public string? SkipReason { get; private set; }

    public async Task InitializeAsync()
    {
        try
        {
            Container = new RedisBuilder().Build();
            await Container.StartAsync();
            IsAvailable = true;
        }
        catch (Exception ex)
        {
            IsAvailable = false;
            SkipReason = $"Redis Testcontainer unavailable ({ex.GetType().Name}): {ex.Message}";
        }
    }

    public async Task DisposeAsync()
    {
        if (Container is not null)
            await Container.DisposeAsync();
    }

    public string ConnectionString
    {
        get
        {
            if (!IsAvailable || Container is null)
                throw new InvalidOperationException(SkipReason ?? "Redis container is not available.");

            return Container.GetConnectionString();
        }
    }
}

[CollectionDefinition(Name)]
public sealed class SqlAndRedisCollection :
    ICollectionFixture<SqlServerContainerFixture>,
    ICollectionFixture<RedisContainerFixture>
{
    public const string Name = "SqlAndRedisCollection";
}
