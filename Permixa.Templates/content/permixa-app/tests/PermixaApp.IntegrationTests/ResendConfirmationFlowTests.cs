using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using PermixaApp.IntegrationTests.Support;
using Xunit;

namespace PermixaApp.IntegrationTests;

public sealed class ResendConfirmationFlowTests : IClassFixture<AppWebApplicationFactory>
{
    private readonly AppWebApplicationFactory _factory;

    public ResendConfirmationFlowTests(AppWebApplicationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task RequireConfirmedEmail_BlocksLogin_UntilOtpConfirmation()
    {
        _factory.Emails.Clear();
        var client = _factory.CreateClient();

        var registerResponse = await client.PostAsJsonAsync("/auth/register", new
        {
            userName = "bob",
            email = "bob@example.test",
            password = TestKeys.UserPassword
        });
        registerResponse.EnsureSuccessStatusCode();
        using var registerDoc = JsonDocument.Parse(await registerResponse.Content.ReadAsStringAsync());
        var userId = registerDoc.RootElement.GetProperty("userId").GetGuid();

        var blockedLogin = await client.PostAsJsonAsync("/auth/login", new
        {
            emailOrUserName = "bob@example.test",
            password = TestKeys.UserPassword
        });
        Assert.False(blockedLogin.IsSuccessStatusCode);

        var requestConfirm = await client.PostAsJsonAsync("/auth/email-confirmation/request", new
        {
            userId
        });
        requestConfirm.EnsureSuccessStatusCode();
        using var challengeDoc = JsonDocument.Parse(await requestConfirm.Content.ReadAsStringAsync());
        var challengeId = challengeDoc.RootElement.GetProperty("challengeId").GetGuid();

        Assert.NotEmpty(_factory.Emails.Sent);
        var otpMatch = Regex.Match(_factory.Emails.Sent[^1].TextBody, @"\b(\d{6})\b");
        Assert.True(otpMatch.Success, "Expected a 6-digit OTP in the captured email body.");

        var confirm = await client.PostAsJsonAsync("/auth/email-confirmation/confirm", new
        {
            challengeId,
            verificationValue = otpMatch.Groups[1].Value
        });
        confirm.EnsureSuccessStatusCode();

        var login = await client.PostAsJsonAsync("/auth/login", new
        {
            emailOrUserName = "bob@example.test",
            password = TestKeys.UserPassword
        });
        login.EnsureSuccessStatusCode();
        var tokens = (await login.Content.ReadFromJsonAsync<AuthTokenResponse>(
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }))!;
        Assert.False(string.IsNullOrWhiteSpace(tokens.AccessToken));

        var me = await _factory.CreateAuthenticatedClient(tokens.AccessToken).GetAsync("/me");
        me.EnsureSuccessStatusCode();
    }
}
