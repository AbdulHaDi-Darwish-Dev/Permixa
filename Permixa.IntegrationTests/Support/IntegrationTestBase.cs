namespace Permixa.IntegrationTests.Support;

[Collection(SqlAndRedisCollection.Name)]
public abstract class IntegrationTestBase
{
    protected IntegrationTestBase(SqlServerContainerFixture sql, RedisContainerFixture redis)
    {
        Sql = sql;
        Redis = redis;
    }

    protected SqlServerContainerFixture Sql { get; }

    protected RedisContainerFixture Redis { get; }

    protected void RequireContainers()
    {
        Skip.If(!Sql.IsAvailable, Sql.SkipReason ?? "SQL Server Testcontainer is unavailable.");
        Skip.If(!Redis.IsAvailable, Redis.SkipReason ?? "Redis Testcontainer is unavailable.");
    }

    protected Task<PermixaTestHost> StartHostAsync(Action<PermixaTestHostOptions>? configure = null) =>
        PermixaTestHost.StartAsync(Sql, Redis, configure);
}
