using Permixa.Application.Verification.Abstractions;
using Permixa.Application.Verification.Models;
using Permixa.Domain.Verification;
using Permixa.Infrastructure;
using Permixa.Infrastructure.Email;
using Permixa.Infrastructure.Email.Templates;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;

namespace Permixa.Infrastructure.Tests;

public sealed class EmailTemplateRendererTests
{
    private readonly EmbeddedEmailTemplateRenderer _renderer = new();

    [Fact]
    public void Otp_RendersHtmlAndText_WithEncodedValues_AndNoLogoWhenAbsent()
    {
        var rendered = _renderer.RenderEmailConfirmationOtp(new EmailConfirmationOtpTemplateModel
        {
            ApplicationName = "Acme <Corp>",
            CompanyName = null,
            LogoUrl = null,
            SupportEmail = "help@example.com",
            VerificationCode = "123456",
            ExpirationMinutes = 5
        });

        Assert.Contains("Your Acme <Corp> verification code", rendered.Subject);
        Assert.Contains("Acme &lt;Corp&gt;", rendered.HtmlBody);
        Assert.DoesNotContain("<img", rendered.HtmlBody);
        Assert.Contains("123456", rendered.HtmlBody);
        Assert.Contains("123456", rendered.TextBody);
        Assert.Contains("5 minutes", rendered.HtmlBody);
        Assert.Contains("help@example.com", rendered.TextBody);
        Assert.DoesNotContain("ChallengeId", rendered.HtmlBody);
    }

    [Fact]
    public void Link_RendersEncodedHref_AndLogoWhenPresent()
    {
        var url = "https://app.example.com/verify?challengeId=abc&token=a+b/c=";
        var rendered = _renderer.RenderEmailConfirmationLink(new EmailConfirmationLinkTemplateModel
        {
            ApplicationName = "Permixa",
            LogoUrl = "https://cdn.example.com/logo.png",
            VerificationUrl = url,
            ExpirationMinutes = 60
        });

        Assert.Contains("Confirm your email for Permixa", rendered.Subject);
        Assert.Contains("href=\"https://app.example.com/verify?challengeId=abc&amp;token=a+b/c=\"", rendered.HtmlBody);
        Assert.Contains("src=\"https://cdn.example.com/logo.png\"", rendered.HtmlBody);
        Assert.Contains(url, rendered.TextBody);
        Assert.DoesNotContain("raw token", rendered.HtmlBody, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PasswordReset_IncludesIgnoreNotice()
    {
        var rendered = _renderer.RenderPasswordReset(new PasswordResetTemplateModel
        {
            ApplicationName = "Permixa",
            ResetUrl = "https://app.example.com/reset?challengeId=1&token=t",
            ExpirationMinutes = 60
        });

        Assert.Contains("Reset your Permixa password", rendered.Subject);
        Assert.Contains("If you did not request a password reset", rendered.HtmlBody);
        Assert.Contains("If you did not request a password reset", rendered.TextBody);
    }

    [Fact]
    public void Html_EncodesScriptInjectionInApplicationName()
    {
        var rendered = _renderer.RenderEmailConfirmationOtp(new EmailConfirmationOtpTemplateModel
        {
            ApplicationName = "<script>alert(1)</script>",
            VerificationCode = "999999",
            ExpirationMinutes = 5
        });

        Assert.DoesNotContain("<script>alert(1)</script>", rendered.HtmlBody);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", rendered.HtmlBody);
    }
}

public sealed class VerificationLinkBuilderTests
{
    [Fact]
    public void ApplyTemplate_EncodesToken_AndRoundTrips()
    {
        var challengeId = Guid.Parse("11111111-1111-1111-1111-111111111111");
        var rawToken = "abc+/=~XYZ";
        var template = "https://app.example.com/verify-email?challengeId={challengeId}&token={token}";

        var url = ConfiguredVerificationLinkBuilder.ApplyTemplate(template, challengeId, rawToken);
        var uri = new Uri(url);
        var query = QueryHelpers.ParseQuery(uri.Query);

        Assert.Equal(challengeId.ToString("D"), query["challengeId"].ToString());
        Assert.Equal(rawToken, query["token"].ToString());
        Assert.Contains(Uri.EscapeDataString(rawToken), url, StringComparison.Ordinal);
    }

    [Fact]
    public void Builder_UsesConfiguredTemplates()
    {
        var options = Options.Create(new PermixaEmailDeliveryOptions
        {
            EmailConfirmationUrlTemplate = "https://a.test/c?challengeId={challengeId}&token={token}",
            PasswordResetUrlTemplate = "https://a.test/r?challengeId={challengeId}&token={token}"
        });
        var builder = new ConfiguredVerificationLinkBuilder(options);
        var id = Guid.NewGuid();

        var confirm = builder.BuildEmailConfirmationLink(id, "tok");
        var reset = builder.BuildPasswordResetLink(id, "tok");

        Assert.StartsWith("https://a.test/c?", confirm);
        Assert.StartsWith("https://a.test/r?", reset);
        Assert.Contains(id.ToString("D"), confirm);
    }
}

public sealed class EmailVerificationDispatcherTests
{
    private static PermixaEmailDeliveryOptions Options() => new()
    {
        FromEmail = "noreply@example.com",
        FromName = "Permixa",
        EmailConfirmationUrlTemplate = "https://app.test/verify?challengeId={challengeId}&token={token}",
        PasswordResetUrlTemplate = "https://app.test/reset?challengeId={challengeId}&token={token}",
        Branding = new PermixaEmailBrandingOptions { ApplicationName = "Permixa App" }
    };

    [Fact]
    public async Task Otp_UsesOtpTemplate_AndIdempotencyKey()
    {
        var sender = new Mock<IEmailSender>();
        EmailOutgoingMessage? sent = null;
        sender.Setup(s => s.SendAsync(It.IsAny<EmailOutgoingMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailOutgoingMessage, CancellationToken>((m, _) => sent = m)
            .Returns(Task.CompletedTask);

        var challengeId = Guid.NewGuid();
        var dispatcher = new EmailVerificationDispatcher(
            new EmbeddedEmailTemplateRenderer(),
            new ConfiguredVerificationLinkBuilder(Microsoft.Extensions.Options.Options.Create(Options())),
            sender.Object,
            Microsoft.Extensions.Options.Options.Create(Options()),
            NullLogger<EmailVerificationDispatcher>.Instance);

        await dispatcher.DispatchAsync(new VerificationDeliveryRequest(
            challengeId,
            "user@example.com",
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "654321",
            DateTime.UtcNow.AddMinutes(5)));

        Assert.NotNull(sent);
        Assert.Equal(EmailVerificationDispatcher.CreateIdempotencyKey(challengeId), sent!.IdempotencyKey);
        Assert.Equal($"permixa-verification/{challengeId:D}", sent.IdempotencyKey);
        Assert.Contains("654321", sent.HtmlBody);
        Assert.Contains("654321", sent.TextBody);
        Assert.DoesNotContain(challengeId.ToString("D"), sent.HtmlBody);
        Assert.Contains("verification code", sent.Subject, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UrlToken_UsesLinkTemplate()
    {
        var sender = new Mock<IEmailSender>();
        EmailOutgoingMessage? sent = null;
        sender.Setup(s => s.SendAsync(It.IsAny<EmailOutgoingMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailOutgoingMessage, CancellationToken>((m, _) => sent = m)
            .Returns(Task.CompletedTask);

        var challengeId = Guid.NewGuid();
        var token = "raw+token/value=";
        var dispatcher = CreateDispatcher(sender.Object);

        await dispatcher.DispatchAsync(new VerificationDeliveryRequest(
            challengeId,
            "user@example.com",
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            token,
            DateTime.UtcNow.AddHours(1)));

        Assert.Contains("Confirm your email", sent!.Subject);
        Assert.Contains(Uri.EscapeDataString(token), sent.HtmlBody);
        Assert.DoesNotContain(">" + token + "<", sent.HtmlBody);
    }

    [Fact]
    public async Task PasswordReset_UsesResetTemplate()
    {
        var sender = new Mock<IEmailSender>();
        EmailOutgoingMessage? sent = null;
        sender.Setup(s => s.SendAsync(It.IsAny<EmailOutgoingMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailOutgoingMessage, CancellationToken>((m, _) => sent = m)
            .Returns(Task.CompletedTask);

        var dispatcher = CreateDispatcher(sender.Object);
        await dispatcher.DispatchAsync(new VerificationDeliveryRequest(
            Guid.NewGuid(),
            "user@example.com",
            VerificationPurpose.PasswordReset,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            "reset-token",
            DateTime.UtcNow.AddHours(1)));

        Assert.Contains("Reset your", sent!.Subject);
        Assert.Contains("did not request a password reset", sent.HtmlBody);
    }

    [Fact]
    public async Task EmailChange_UsesConfirmationLinkTemplate_ToDestination()
    {
        var sender = new Mock<IEmailSender>();
        EmailOutgoingMessage? sent = null;
        sender.Setup(s => s.SendAsync(It.IsAny<EmailOutgoingMessage>(), It.IsAny<CancellationToken>()))
            .Callback<EmailOutgoingMessage, CancellationToken>((m, _) => sent = m)
            .Returns(Task.CompletedTask);

        var dispatcher = CreateDispatcher(sender.Object);
        await dispatcher.DispatchAsync(new VerificationDeliveryRequest(
            Guid.NewGuid(),
            "pending@example.com",
            VerificationPurpose.EmailChange,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            "change-token",
            DateTime.UtcNow.AddHours(1)));

        Assert.Equal("pending@example.com", sent!.To);
        Assert.Contains("Confirm your email", sent.Subject);
        Assert.Contains(Uri.EscapeDataString("change-token"), sent.HtmlBody);
    }

    [Fact]
    public async Task UnsupportedCombination_FailsClearly()
    {
        var dispatcher = CreateDispatcher(Mock.Of<IEmailSender>());
        var ex = await Assert.ThrowsAsync<EmailDeliveryException>(() => dispatcher.DispatchAsync(
            new VerificationDeliveryRequest(
                Guid.NewGuid(),
                "user@example.com",
                VerificationPurpose.TwoFactorAuthentication,
                VerificationMethod.Otp,
                VerificationChannel.Email,
                "x",
                DateTime.UtcNow.AddMinutes(5))));

        Assert.Contains("Unsupported", ex.Message);
    }

    private static EmailVerificationDispatcher CreateDispatcher(IEmailSender sender) =>
        new(
            new EmbeddedEmailTemplateRenderer(),
            new ConfiguredVerificationLinkBuilder(Microsoft.Extensions.Options.Options.Create(Options())),
            sender,
            Microsoft.Extensions.Options.Options.Create(Options()),
            NullLogger<EmailVerificationDispatcher>.Instance);
}

public sealed class EmailDeliveryDiTests
{
    [Fact]
    public void AddPermixaVerification_DoesNotRequireEmailDelivery()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString =
                "Server=localhost;Database=PermixaDiProbe;Trusted_Connection=True;TrustServerCertificate=True";
        });
        services.AddPermixaVerification();

        using var sp = services.BuildServiceProvider();
        Assert.Null(sp.GetService<IVerificationDispatcher>());
        Assert.Null(sp.GetService<IEmailSender>());
    }

    [Fact]
    public void AddPermixaEmailDelivery_RegistersNeutralPipeline_WithoutTransport()
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

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        Assert.IsType<EmbeddedEmailTemplateRenderer>(
            scope.ServiceProvider.GetRequiredService<IEmailTemplateRenderer>());
        Assert.IsType<ConfiguredVerificationLinkBuilder>(
            scope.ServiceProvider.GetRequiredService<IVerificationLinkBuilder>());
        Assert.Null(scope.ServiceProvider.GetService<IEmailSender>());
        // Dispatcher is registered but cannot activate until an IEmailSender transport is supplied.
        Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IVerificationDispatcher>());
    }

    [Fact]
    public void AddPermixaEmailDelivery_WithHostIEmailSender_ActivatesDispatcher()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString =
                "Server=localhost;Database=PermixaDiProbe3;Trusted_Connection=True;TrustServerCertificate=True";
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
        services.AddScoped<IEmailSender>(_ => Mock.Of<IEmailSender>());

        using var sp = services.BuildServiceProvider();
        using var scope = sp.CreateScope();
        Assert.IsType<EmailVerificationDispatcher>(
            scope.ServiceProvider.GetRequiredService<IVerificationDispatcher>());
    }

    [Fact]
    public void AddPermixaEmailDelivery_MissingFromEmail_FailsFast()
    {
        var services = new ServiceCollection();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddPermixaEmailDelivery(o =>
            {
                o.FromName = "Permixa";
                o.Branding.ApplicationName = "Permixa";
                o.EmailConfirmationUrlTemplate = "https://app.example.com/verify?challengeId={challengeId}&token={token}";
                o.PasswordResetUrlTemplate = "https://app.example.com/reset?challengeId={challengeId}&token={token}";
            }));
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class VerificationEmailUrlRoundTripTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;
    private ServiceProvider _sp = null!;
    private CapturingVerificationDispatcher _capture = null!;

    public VerificationEmailUrlRoundTripTests(SqlServerContainerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    public async Task InitializeAsync()
    {
        _connectionString = _sqlServer.CreateUniqueDatabaseConnectionString();
        _capture = new CapturingVerificationDispatcher();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o => o.ConnectionString = _connectionString);
        services.AddPermixaAuthentication(o =>
        {
            o.Jwt.Issuer = TestJwtKeys.Issuer;
            o.Jwt.Audience = TestJwtKeys.Audience;
            o.Jwt.PrivateKeyPem = TestJwtKeys.PrivateKeyPem;
        });
        services.AddPermixaVerification();
        services.AddSingleton<IVerificationDispatcher>(_capture);
        _sp = services.BuildServiceProvider();
        using var scope = _sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _sp.DisposeAsync();

    [Fact]
    public async Task EmailConfirmationUrl_FromTemplate_RoundTripsThroughConfirmEmail()
    {
        Guid userId;
        using (var scope = _sp.CreateScope())
        {
            var register = scope.ServiceProvider.GetRequiredService<Application.Authentication.Register.RegisterUserUseCase>();
            var created = await register.ExecuteAsync(new Application.Authentication.Models.RegisterRequest
            {
                UserName = "linkuser",
                Email = "linkuser@example.com",
                Password = "Passw0rd!"
            });
            Assert.True(created.IsSuccess);
            userId = created.Value.UserId;
        }

        _capture.Deliveries.Clear();
        Guid challengeId;
        string rawToken;
        using (var scope = _sp.CreateScope())
        {
            var request = scope.ServiceProvider.GetRequiredService<Application.Verification.EmailConfirmation.RequestEmailConfirmationUseCase>();
            var issued = await request.ExecuteAsync(new Application.Verification.EmailConfirmation.RequestEmailConfirmationRequest
            {
                UserId = userId,
                Method = VerificationMethod.UrlToken
            });
            Assert.True(issued.IsSuccess);
            challengeId = issued.Value.ChallengeId!.Value;
            rawToken = _capture.Deliveries[^1].RawVerificationValue;
        }

        var url = ConfiguredVerificationLinkBuilder.ApplyTemplate(
            "https://app.example.com/verify-email?challengeId={challengeId}&token={token}",
            challengeId,
            rawToken);
        var query = QueryHelpers.ParseQuery(new Uri(url).Query);
        var decodedChallengeId = Guid.Parse(query["challengeId"].ToString());
        var decodedToken = query["token"].ToString();
        Assert.Equal(challengeId, decodedChallengeId);
        Assert.Equal(rawToken, decodedToken);

        using (var scope = _sp.CreateScope())
        {
            var confirm = scope.ServiceProvider.GetRequiredService<Application.Verification.EmailConfirmation.ConfirmEmailUseCase>();
            var result = await confirm.ExecuteAsync(new Application.Verification.EmailConfirmation.ConfirmEmailRequest
            {
                ChallengeId = decodedChallengeId,
                VerificationValue = decodedToken
            });
            Assert.True(result.IsSuccess, result.Error?.Description);
        }
    }
}
