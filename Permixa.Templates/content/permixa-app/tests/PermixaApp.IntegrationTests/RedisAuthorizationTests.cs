using PermixaApp.IntegrationTests.Support;
using Xunit;

namespace PermixaApp.IntegrationTests;

#if (redis)
public sealed class RedisAuthorizationTests : IClassFixture<AppWebApplicationFactory>
{
    private readonly AppWebApplicationFactory _factory;

    public RedisAuthorizationTests(AppWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProtectedEndpoint_Works_WithRedis_And_AfterRedisFailure()
    {
        var client = _factory.CreateClient();
        var tokens = await client.LoginAsOwnerAsync();
        var authed = _factory.CreateAuthenticatedClient(tokens.AccessToken);

        (await authed.GetAsync("/sample/protected")).EnsureSuccessStatusCode();

        // Fail open: stop Redis; authorization must still resolve from SQL (cache miss).
        await _factory.StopRedisAsync();

        (await authed.GetAsync("/sample/protected")).EnsureSuccessStatusCode();
        (await authed.GetAsync("/me")).EnsureSuccessStatusCode();
    }
}
#endif
