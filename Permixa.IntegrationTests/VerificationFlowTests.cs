using System.Net.Http.Json;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Verification;
using Permixa.Application.Verification.EmailConfirmation;
using Permixa.Application.Verification.PasswordReset;
using Permixa.Domain.Verification;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Permixa.IntegrationTests.Support;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.IntegrationTests;

public sealed class VerificationFlowTests : IntegrationTestBase
{
    public VerificationFlowTests(SqlServerContainerFixture sql, RedisContainerFixture redis)
        : base(sql, redis)
    {
    }

    [SkippableFact]
    public async Task EmailConfirmation_Otp_ConsumesChallenge()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var user = await host.RegisterAsync("otpuser", "otpuser@permixa.test", TestKeys.UserPassword);

        var requested = await RequestConfirmationAsync(host, user.UserId, "Otp");
        Assert.False(requested.AlreadyConfirmed);
        Assert.NotNull(requested.ChallengeId);

        var otp = host.Emails.ExtractOtp();
        Assert.Equal(
            "permixa-verification/" + requested.ChallengeId!.Value.ToString("D"),
            host.Emails.Last.IdempotencyKey);

        var confirm = await host.Client.PostAsJsonAsync("/email-confirmation/confirm", new ConfirmEmailRequest
        {
            ChallengeId = requested.ChallengeId!.Value,
            VerificationValue = otp
        });
        Assert.True(confirm.IsSuccessStatusCode);

        await host.ExecuteScopedAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var identity = await users.FindByIdAsync(user.UserId.ToString());
            Assert.True(identity!.EmailConfirmed);

            var challenge = await db.VerificationChallenges.SingleAsync();
            Assert.True(challenge.IsConsumed);
            Assert.DoesNotContain(otp, SerializeChallenge(challenge));
        });
    }

    [SkippableFact]
    public async Task EmailConfirmation_UrlToken_PreservesExactToken()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var user = await host.RegisterAsync("urluser", "urluser@permixa.test", TestKeys.UserPassword);

        var requested = await RequestConfirmationAsync(host, user.UserId, "UrlToken");
        var (challengeId, token, url) = host.Emails.ExtractLink("Confirm your email address by opening this link:");
        Assert.Equal(requested.ChallengeId, challengeId);
        Assert.Contains(Uri.EscapeDataString(token), url, StringComparison.Ordinal);

        var confirm = await host.Client.PostAsJsonAsync("/email-confirmation/confirm", new ConfirmEmailRequest
        {
            ChallengeId = challengeId,
            VerificationValue = token
        });
        Assert.True(confirm.IsSuccessStatusCode);

        await host.ExecuteScopedAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.True((await users.FindByIdAsync(user.UserId.ToString()))!.EmailConfirmed);
            Assert.True((await db.VerificationChallenges.SingleAsync()).IsConsumed);
            Assert.DoesNotContain(token, SerializeChallenge(await db.VerificationChallenges.SingleAsync()));
        });
    }

    [SkippableFact]
    public async Task DestinationBinding_RejectsOldChallengeAfterEmailChange()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var user = await host.RegisterAsync("binduser", "binduser@permixa.test", TestKeys.UserPassword);
        var requested = await RequestConfirmationAsync(host, user.UserId, "Otp");
        var otp = host.Emails.ExtractOtp();

        await host.ExecuteScopedAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var identity = await users.FindByIdAsync(user.UserId.ToString());
            Assert.True((await users.SetEmailAsync(identity!, "after@permixa.test")).Succeeded);
        });

        var confirm = await host.Client.PostAsJsonAsync("/email-confirmation/confirm", new ConfirmEmailRequest
        {
            ChallengeId = requested.ChallengeId!.Value,
            VerificationValue = otp
        });
        await ProblemJson.ReadAndAssertAsync(confirm, 400, VerificationErrors.DestinationMismatch.Code);

        await host.ExecuteScopedAsync(async sp =>
        {
            var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
            var identity = await users.FindByIdAsync(user.UserId.ToString());
            Assert.False(identity!.EmailConfirmed);
            Assert.Equal("after@permixa.test", identity.Email);
        });
    }

    [SkippableFact]
    public async Task OtpExhaustion_InvalidatesChallenge()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var user = await host.RegisterAsync("abuse", "abuse@permixa.test", TestKeys.UserPassword);
        var requested = await RequestConfirmationAsync(host, user.UserId, "Otp");
        var otp = host.Emails.ExtractOtp();

        for (var i = 1; i <= 4; i++)
        {
            var attempt = await host.Client.PostAsJsonAsync("/email-confirmation/confirm", new ConfirmEmailRequest
            {
                ChallengeId = requested.ChallengeId!.Value,
                VerificationValue = "000000"
            });
            await ProblemJson.ReadAndAssertAsync(attempt, 400, VerificationErrors.InvalidCode.Code);

            await host.ExecuteScopedAsync(async sp =>
            {
                var challenge = await sp.GetRequiredService<ApplicationDbContext>().VerificationChallenges.SingleAsync();
                Assert.True(challenge.IsActive());
                Assert.Equal(i, challenge.FailedAttempts);
            });
        }

        var fifth = await host.Client.PostAsJsonAsync("/email-confirmation/confirm", new ConfirmEmailRequest
        {
            ChallengeId = requested.ChallengeId!.Value,
            VerificationValue = "000000"
        });
        await ProblemJson.ReadAndAssertAsync(fifth, 400, VerificationErrors.TooManyAttempts.Code);

        var late = await host.Client.PostAsJsonAsync("/email-confirmation/confirm", new ConfirmEmailRequest
        {
            ChallengeId = requested.ChallengeId!.Value,
            VerificationValue = otp
        });
        await ProblemJson.ReadAndAssertAsync(late, 400, VerificationErrors.Invalidated.Code);

        await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var challenge = await db.VerificationChallenges.SingleAsync();
            Assert.True(challenge.IsInvalidated);
            Assert.DoesNotContain(otp, SerializeChallenge(challenge));
        });
    }

    [SkippableFact]
    public async Task Cooldown_ThenReissue_InvalidatesPreviousChallenge()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var user = await host.RegisterAsync("cool", "cool@permixa.test", TestKeys.UserPassword);

        var first = await RequestConfirmationAsync(host, user.UserId, "Otp");
        var tooSoon = await host.Client.PostAsJsonAsync("/email-confirmation/request", new RequestEmailConfirmationHttpRequest
        {
            UserId = user.UserId,
            Method = "Otp"
        });
        await ProblemJson.ReadAndAssertAsync(tooSoon, 409, VerificationErrors.ResendTooSoon.Code);

        await host.ExecuteScopedAsync(async sp =>
        {
            var challenge = await sp.GetRequiredService<ApplicationDbContext>().VerificationChallenges.SingleAsync();
            Assert.Equal(first.ChallengeId, challenge.Id);
            Assert.True(challenge.IsActive());
        });

        host.Clock.Advance(TimeSpan.FromSeconds(61));
        var second = await RequestConfirmationAsync(host, user.UserId, "Otp");
        Assert.NotEqual(first.ChallengeId, second.ChallengeId);

        await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            var previous = await db.VerificationChallenges.SingleAsync(c => c.Id == first.ChallengeId);
            var current = await db.VerificationChallenges.SingleAsync(c => c.Id == second.ChallengeId);
            Assert.True(previous.IsInvalidated);
            Assert.True(current.IsActive(host.Clock.UtcNow));
        });
    }

    [SkippableFact]
    public async Task ForgotPassword_DoesNotEnumerateAccounts()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        await host.RegisterAsync("known", "known@permixa.test", TestKeys.UserPassword);

        var known = await host.Client.PostAsJsonAsync("/password-reset/request", new RequestPasswordResetRequest
        {
            Email = "known@permixa.test"
        });
        var unknown = await host.Client.PostAsJsonAsync("/password-reset/request", new RequestPasswordResetRequest
        {
            Email = "missing@permixa.test"
        });

        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(known.Content.Headers.ContentType?.MediaType, unknown.Content.Headers.ContentType?.MediaType);
        var knownJson = await known.Content.ReadAsStringAsync();
        var unknownJson = await unknown.Content.ReadAsStringAsync();
        Assert.Equal(knownJson, unknownJson);
        Assert.Contains("accepted", knownJson, StringComparison.OrdinalIgnoreCase);

        Assert.Single(host.Emails.Messages);
        await host.ExecuteScopedAsync(async sp =>
        {
            var db = sp.GetRequiredService<ApplicationDbContext>();
            Assert.Equal(1, await db.VerificationChallenges.CountAsync());
            Assert.Equal("known@permixa.test", (await db.VerificationChallenges.SingleAsync()).Destination);
        });
    }

    [SkippableFact]
    public async Task PasswordReset_EndToEnd_AndRejectsInvalidOrConsumed()
    {
        RequireContainers();
        await using var host = await StartHostAsync();
        var user = await host.RegisterAsync("reset", "reset@permixa.test", TestKeys.UserPassword);

        await host.Client.PostAsJsonAsync("/password-reset/request", new RequestPasswordResetRequest
        {
            Email = "reset@permixa.test"
        });
        var (challengeId, token, _) = host.Emails.ExtractLink("Reset your password by opening this link:");

        var invalid = await host.Client.PostAsJsonAsync("/password-reset/reset", new ResetPasswordWithVerificationRequest
        {
            ChallengeId = challengeId,
            Token = "not-the-token",
            NewPassword = "NewPass1!"
        });
        await ProblemJson.ReadAndAssertAsync(invalid, 400, VerificationErrors.InvalidToken.Code);
        await host.LoginAsync("reset@permixa.test", TestKeys.UserPassword);

        var reset = await host.Client.PostAsJsonAsync("/password-reset/reset", new ResetPasswordWithVerificationRequest
        {
            ChallengeId = challengeId,
            Token = token,
            NewPassword = "NewPass1!"
        });
        Assert.True(reset.IsSuccessStatusCode);

        var oldPassword = await host.LoginRawAsync("reset@permixa.test", TestKeys.UserPassword);
        await ProblemJson.ReadAndAssertAsync(oldPassword, 401, AuthenticationErrors.InvalidCredentials.Code);
        await host.LoginAsync("reset@permixa.test", "NewPass1!");

        var reused = await host.Client.PostAsJsonAsync("/password-reset/reset", new ResetPasswordWithVerificationRequest
        {
            ChallengeId = challengeId,
            Token = token,
            NewPassword = "Another1!"
        });
        await ProblemJson.ReadAndAssertAsync(reused, 409, VerificationErrors.AlreadyConsumed.Code);
    }

    [SkippableFact]
    public async Task DispatchFailure_InvalidatesChallenge()
    {
        RequireContainers();
        await using var host = await StartHostAsync(o => o.ThrowOnEmail = true);
        var user = await host.RegisterAsync("failmail", "failmail@permixa.test", TestKeys.UserPassword);

        var response = await host.Client.PostAsJsonAsync("/email-confirmation/request", new RequestEmailConfirmationHttpRequest
        {
            UserId = user.UserId,
            Method = "Otp"
        });
        var problem = await ProblemJson.ReadAndAssertAsync(response, 503, "Email.DeliveryFailed");
        Assert.DoesNotContain("Test email sender refused delivery", problem.GetRawText(), StringComparison.Ordinal);

        await host.ExecuteScopedAsync(async sp =>
        {
            var challenge = await sp.GetRequiredService<ApplicationDbContext>().VerificationChallenges.SingleAsync();
            Assert.True(challenge.IsInvalidated);
        });
        Assert.Empty(host.Emails.Messages);
    }

    private static async Task<RequestEmailConfirmationResult> RequestConfirmationAsync(
        PermixaTestHost host,
        Guid userId,
        string method)
    {
        var response = await host.Client.PostAsJsonAsync("/email-confirmation/request", new RequestEmailConfirmationHttpRequest
        {
            UserId = userId,
            Method = method
        });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<RequestEmailConfirmationResult>())!;
    }

    private static string SerializeChallenge(VerificationChallenge challenge) =>
        string.Join('|',
            challenge.Id,
            challenge.UserId,
            challenge.Destination,
            challenge.Purpose,
            challenge.Method,
            challenge.Channel,
            challenge.FailedAttempts,
            challenge.ConsumedAtUtc,
            challenge.InvalidatedAtUtc);
}
