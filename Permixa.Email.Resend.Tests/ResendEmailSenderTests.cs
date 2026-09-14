using Permixa.Application.Verification.Abstractions;
using Permixa.Infrastructure;
using Permixa.Infrastructure.Email;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Resend;

namespace Permixa.Email.Resend.Tests;

public sealed class ResendEmailSenderTests
{
    [Fact]
    public async Task MapsNeutralMessage_ToResendApi_WithIdempotencyKey()
    {
        var resend = new Mock<IResend>();
        string? idempotency = null;
        EmailMessage? message = null;
        resend.Setup(r => r.EmailSendAsync(
                It.IsAny<string>(),
                It.IsAny<EmailMessage>(),
                It.IsAny<CancellationToken>()))
            .Callback<string, EmailMessage, CancellationToken>((key, msg, _) =>
            {
                idempotency = key;
                message = msg;
            })
            .ReturnsAsync(new ResendResponse<Guid>(Guid.NewGuid(), new ResendRateLimit()));

        var sut = new ResendEmailSender(
            resend.Object,
            NullLogger<ResendEmailSender>.Instance);
        await sut.SendAsync(new EmailOutgoingMessage(
            "user@example.com",
            "Permixa <noreply@example.com>",
            "Subject",
            "<p>html</p>",
            "text",
            "permixa-verification/11111111-1111-1111-1111-111111111111"));

        Assert.Equal("permixa-verification/11111111-1111-1111-1111-111111111111", idempotency);
        Assert.Equal("user@example.com", message!.To![0].Email);
        Assert.Equal("noreply@example.com", message.From!.Email);
        Assert.Equal("Permixa", message.From.DisplayName);
        Assert.Equal("Subject", message.Subject);
        Assert.Equal("<p>html</p>", message.HtmlBody);
        Assert.Equal("text", message.TextBody);
    }

    [Fact]
    public async Task ProviderFailure_IsWrapped_WithoutExposingApiKey()
    {
        var resend = new Mock<IResend>();
        resend.Setup(r => r.EmailSendAsync(It.IsAny<string>(), It.IsAny<EmailMessage>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ResendException(System.Net.HttpStatusCode.Unauthorized, ErrorType.InvalidApiKey, "bad key", new ResendRateLimit()));

        var sut = new ResendEmailSender(
            resend.Object,
            NullLogger<ResendEmailSender>.Instance);
        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => sut.SendAsync(
            new EmailOutgoingMessage(
                "a@b.com",
                "Permixa <noreply@example.com>",
                "s",
                "h",
                "t",
                "permixa-verification/" + Guid.NewGuid().ToString("D"))));

        Assert.Equal("Email delivery failed.", ex.Message);
        Assert.DoesNotContain("re_secret_should_not_leak", ex.ToString());
    }
}

public sealed class ResendEmailDiTests
{
    [Fact]
    public void AddPermixaResendEmail_RegistersTransport_Only()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString =
                "Server=localhost;Database=PermixaDiProbe2;Trusted_Connection=True;TrustServerCertificate=True";
        });
        services.AddPermixaVerification();
        services.AddPermixaEmailDelivery(o =>
        {
            o.FromEmail = "noreply@example.com";
            o.FromName = "Permixa";
            o.Branding.ApplicationName = "Permixa";
            o.EmailConfirmationUrlTemplate = "https://app.example.com/verify?challengeId={challengeId}&token={token}";
            o.PasswordResetUrlTemplate = "https://app.example.com/reset?challengeId={challengeId}&token={token}";
        });
        services.AddPermixaResendEmail(o =>
        {
            o.ApiKey = "re_test_key";
        });

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        Assert.IsType<EmailVerificationDispatcher>(
            scope.ServiceProvider.GetRequiredService<IVerificationDispatcher>());
        Assert.IsType<ResendEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
        Assert.IsType<EmbeddedEmailTemplateRenderer>(
            scope.ServiceProvider.GetRequiredService<IEmailTemplateRenderer>());
        Assert.IsType<ConfiguredVerificationLinkBuilder>(
            scope.ServiceProvider.GetRequiredService<IVerificationLinkBuilder>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IResend>());
    }

    [Fact]
    public void AddPermixaResendEmail_MissingApiKey_FailsFast()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddPermixaResendEmail(o =>
            {
                o.ApiKey = " ";
            }));
    }

    [Fact]
    public void AddPermixaResendEmail_DoesNotRegisterNeutralPipelineAlone()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaResendEmail(o => o.ApiKey = "re_test_key");

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        Assert.IsType<ResendEmailSender>(scope.ServiceProvider.GetRequiredService<IEmailSender>());
        Assert.Null(scope.ServiceProvider.GetService<IEmailTemplateRenderer>());
        Assert.Null(scope.ServiceProvider.GetService<IVerificationDispatcher>());
    }
}
