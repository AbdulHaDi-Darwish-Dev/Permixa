using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Verification;
using Permixa.Application.Verification.Abstractions;
using Permixa.Application.Verification.EmailConfirmation;
using Permixa.Application.Verification.Models;
using Permixa.Application.Verification.PasswordReset;
using Permixa.Domain.Verification;
using Microsoft.Extensions.Options;
using Moq;

namespace Permixa.Application.Tests.Verification;

public sealed class RequestEmailConfirmationUseCaseTests
{
    [Fact]
    public async Task AlreadyConfirmed_ReturnsIdempotentSuccess_WithoutIssuing()
    {
        var userId = Guid.NewGuid();
        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("alice@example.com");
        emails.Setup(e => e.IsEmailConfirmedAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var tokens = new Mock<IVerificationTokenProvider>(MockBehavior.Strict);
        var issuer = new VerificationChallengeIssuer(
            Mock.Of<IVerificationChallengeRepository>(),
            tokens.Object,
            Mock.Of<IVerificationDispatcher>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(),
            Mock.Of<IPersistenceExceptionClassifier>(),
            Options.Create(new PermixaVerificationOptions()));

        var sut = new RequestEmailConfirmationUseCase(emails.Object, issuer);
        var result = await sut.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.Otp
        });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.AlreadyConfirmed);
        Assert.Null(result.Value.ChallengeId);
        tokens.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UnsupportedMethod_Fails()
    {
        var emails = new Mock<IIdentityUserEmailReader>();
        var issuer = new VerificationChallengeIssuer(
            Mock.Of<IVerificationChallengeRepository>(),
            Mock.Of<IVerificationTokenProvider>(),
            Mock.Of<IVerificationDispatcher>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(),
            Mock.Of<IPersistenceExceptionClassifier>(),
            Options.Create(new PermixaVerificationOptions()));

        var sut = new RequestEmailConfirmationUseCase(emails.Object, issuer);

        var result = await sut.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = Guid.NewGuid(),
            Method = (VerificationMethod)99
        });

        Assert.True(result.IsFailure);
        Assert.Equal(VerificationErrors.UnsupportedMethod, result.Error);
    }
}

public sealed class ConfirmEmailUseCaseTests
{
    private static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Otp_Invalid_IncrementsFailedAttempts()
    {
        var userId = Guid.NewGuid();
        var challenge = VerificationChallenge.Create(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "alice@example.com",
            Now.AddMinutes(5),
            createdAtUtc: Now);

        var repo = new Mock<IVerificationChallengeRepository>();
        repo.Setup(r => r.GetByIdAsync(challenge.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var tokens = new Mock<IVerificationTokenProvider>();
        tokens.Setup(t => t.ValidateAsync(
                userId,
                VerificationPurpose.EmailConfirmation,
                VerificationMethod.Otp,
                "bad",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("alice@example.com");

        var confirmation = new Mock<IIdentityEmailConfirmation>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var sut = new ConfirmEmailUseCase(
            repo.Object,
            tokens.Object,
            emails.Object,
            confirmation.Object,
            uow.Object,
            clock.Object,
            Options.Create(new PermixaVerificationOptions { MaxOtpAttempts = 5 }));

        var result = await sut.ExecuteAsync(new ConfirmEmailRequest
        {
            ChallengeId = challenge.Id,
            VerificationValue = "bad"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(VerificationErrors.InvalidCode, result.Error);
        Assert.Equal(1, challenge.FailedAttempts);
        Assert.False(challenge.IsInvalidated);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        confirmation.Verify(
            c => c.MarkEmailConfirmedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Otp_FifthFailure_Invalidates_AndReturnsTooManyAttempts()
    {
        var userId = Guid.NewGuid();
        var challenge = VerificationChallenge.Reconstitute(
            Guid.NewGuid(),
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "alice@example.com",
            Now,
            Now.AddMinutes(5),
            consumedAtUtc: null,
            failedAttempts: 4,
            invalidatedAtUtc: null);

        var repo = new Mock<IVerificationChallengeRepository>();
        repo.Setup(r => r.GetByIdAsync(challenge.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var tokens = new Mock<IVerificationTokenProvider>();
        tokens.Setup(t => t.ValidateAsync(
                It.IsAny<Guid>(),
                It.IsAny<VerificationPurpose>(),
                It.IsAny<VerificationMethod>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("alice@example.com");

        var sut = new ConfirmEmailUseCase(
            repo.Object,
            tokens.Object,
            emails.Object,
            Mock.Of<IIdentityEmailConfirmation>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(c => c.UtcNow == Now),
            Options.Create(new PermixaVerificationOptions { MaxOtpAttempts = 5 }));

        var result = await sut.ExecuteAsync(new ConfirmEmailRequest
        {
            ChallengeId = challenge.Id,
            VerificationValue = "bad"
        });

        Assert.Equal(VerificationErrors.TooManyAttempts, result.Error);
        Assert.Equal(5, challenge.FailedAttempts);
        Assert.True(challenge.IsInvalidated);
    }

    [Fact]
    public async Task UrlToken_Invalid_DoesNotIncrementAttempts()
    {
        var userId = Guid.NewGuid();
        var challenge = VerificationChallenge.Create(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            "alice@example.com",
            Now.AddHours(1),
            createdAtUtc: Now);

        var repo = new Mock<IVerificationChallengeRepository>();
        repo.Setup(r => r.GetByIdAsync(challenge.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var confirmation = new Mock<IIdentityEmailConfirmation>();
        confirmation.Setup(c => c.ConfirmEmailWithTokenAsync(userId, "bad", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("alice@example.com");

        var sut = new ConfirmEmailUseCase(
            repo.Object,
            Mock.Of<IVerificationTokenProvider>(),
            emails.Object,
            confirmation.Object,
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(c => c.UtcNow == Now),
            Options.Create(new PermixaVerificationOptions()));

        var result = await sut.ExecuteAsync(new ConfirmEmailRequest
        {
            ChallengeId = challenge.Id,
            VerificationValue = "bad"
        });

        Assert.Equal(VerificationErrors.InvalidToken, result.Error);
        Assert.Equal(0, challenge.FailedAttempts);
        Assert.False(challenge.IsInvalidated);
    }

    [Fact]
    public async Task EmailChanged_RejectsWithDestinationMismatch()
    {
        var userId = Guid.NewGuid();
        var challenge = VerificationChallenge.Create(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "old@example.com",
            Now.AddMinutes(5),
            createdAtUtc: Now);

        var repo = new Mock<IVerificationChallengeRepository>();
        repo.Setup(r => r.GetByIdAsync(challenge.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("new@example.com");

        var sut = new ConfirmEmailUseCase(
            repo.Object,
            Mock.Of<IVerificationTokenProvider>(),
            emails.Object,
            Mock.Of<IIdentityEmailConfirmation>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(c => c.UtcNow == Now),
            Options.Create(new PermixaVerificationOptions()));

        var result = await sut.ExecuteAsync(new ConfirmEmailRequest
        {
            ChallengeId = challenge.Id,
            VerificationValue = "123456"
        });

        Assert.Equal(VerificationErrors.DestinationMismatch, result.Error);
    }

    [Fact]
    public async Task Expired_Rejects()
    {
        var userId = Guid.NewGuid();
        var challenge = VerificationChallenge.Create(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "alice@example.com",
            Now.AddMinutes(5),
            createdAtUtc: Now);

        var repo = new Mock<IVerificationChallengeRepository>();
        repo.Setup(r => r.GetByIdAsync(challenge.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var sut = new ConfirmEmailUseCase(
            repo.Object,
            Mock.Of<IVerificationTokenProvider>(),
            Mock.Of<IIdentityUserEmailReader>(),
            Mock.Of<IIdentityEmailConfirmation>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(c => c.UtcNow == Now.AddMinutes(6)),
            Options.Create(new PermixaVerificationOptions()));

        var result = await sut.ExecuteAsync(new ConfirmEmailRequest
        {
            ChallengeId = challenge.Id,
            VerificationValue = "123456"
        });

        Assert.Equal(VerificationErrors.Expired, result.Error);
    }
}

public sealed class VerificationChallengeIssuerTests
{
    private static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Cooldown_BlocksWithoutInvalidating()
    {
        var userId = Guid.NewGuid();
        var existing = VerificationChallenge.Create(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "alice@example.com",
            Now.AddMinutes(5),
            createdAtUtc: Now.AddSeconds(-30));

        var repo = new Mock<IVerificationChallengeRepository>();
        repo.Setup(r => r.GetOpenAsync(userId, VerificationPurpose.EmailConfirmation, "alice@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { existing });

        var tokens = new Mock<IVerificationTokenProvider>();
        var dispatcher = new Mock<IVerificationDispatcher>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var sut = new VerificationChallengeIssuer(
            repo.Object,
            tokens.Object,
            dispatcher.Object,
            uow.Object,
            clock.Object,
            Mock.Of<IPersistenceExceptionClassifier>(),
            Options.Create(new PermixaVerificationOptions
            {
                ResendCooldown = TimeSpan.FromSeconds(60)
            }));

        var result = await sut.IssueAsync(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "alice@example.com");

        Assert.Equal(VerificationErrors.ResendTooSoon, result.Error);
        Assert.False(existing.IsInvalidated);
        tokens.Verify(
            t => t.GenerateAsync(It.IsAny<Guid>(), It.IsAny<VerificationPurpose>(), It.IsAny<VerificationMethod>(), It.IsAny<CancellationToken>()),
            Times.Never);
        dispatcher.Verify(
            d => d.DispatchAsync(It.IsAny<VerificationDeliveryRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Issue_PersistsBeforeDispatch_AndBindsChallengeId()
    {
        var userId = Guid.NewGuid();
        var repo = new Mock<IVerificationChallengeRepository>();
        repo.Setup(r => r.GetOpenAsync(It.IsAny<Guid>(), It.IsAny<VerificationPurpose>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<VerificationChallenge>());

        VerificationChallenge? added = null;
        repo.Setup(r => r.AddAsync(It.IsAny<VerificationChallenge>(), It.IsAny<CancellationToken>()))
            .Callback<VerificationChallenge, CancellationToken>((c, _) => added = c)
            .Returns(Task.CompletedTask);

        var tokens = new Mock<IVerificationTokenProvider>();
        tokens.Setup(t => t.GenerateAsync(userId, VerificationPurpose.EmailConfirmation, VerificationMethod.Otp, It.IsAny<CancellationToken>()))
            .ReturnsAsync("raw-otp");

        var saveOrder = 0;
        var dispatchOrder = 0;
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() => saveOrder = 1)
            .ReturnsAsync(1);

        VerificationDeliveryRequest? delivery = null;
        var dispatcher = new Mock<IVerificationDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<VerificationDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<VerificationDeliveryRequest, CancellationToken>((r, _) =>
            {
                dispatchOrder = saveOrder + 1;
                delivery = r;
            })
            .Returns(Task.CompletedTask);

        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var sut = new VerificationChallengeIssuer(
            repo.Object,
            tokens.Object,
            dispatcher.Object,
            uow.Object,
            clock.Object,
            Mock.Of<IPersistenceExceptionClassifier>(),
            Options.Create(new PermixaVerificationOptions
            {
                OtpLifetime = TimeSpan.FromMinutes(5)
            }));

        var result = await sut.IssueAsync(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "alice@example.com");

        Assert.True(result.IsSuccess);
        Assert.NotNull(added);
        Assert.Equal(added!.Id, result.Value);
        Assert.Equal(added.Id, delivery!.ChallengeId);
        Assert.Equal("raw-otp", delivery.RawVerificationValue);
        Assert.Equal(Now.AddMinutes(5), added.ExpiresAtUtc);
        Assert.True(dispatchOrder > saveOrder);
        Assert.DoesNotContain("raw-otp", typeof(VerificationChallenge).GetProperties().Select(p => p.Name));
    }

    [Fact]
    public async Task DispatchFailure_InvalidatesChallenge()
    {
        var userId = Guid.NewGuid();
        var repo = new Mock<IVerificationChallengeRepository>();
        repo.Setup(r => r.GetOpenAsync(It.IsAny<Guid>(), It.IsAny<VerificationPurpose>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<VerificationChallenge>());

        VerificationChallenge? added = null;
        repo.Setup(r => r.AddAsync(It.IsAny<VerificationChallenge>(), It.IsAny<CancellationToken>()))
            .Callback<VerificationChallenge, CancellationToken>((c, _) => added = c)
            .Returns(Task.CompletedTask);

        var tokens = new Mock<IVerificationTokenProvider>();
        tokens.Setup(t => t.GenerateAsync(It.IsAny<Guid>(), It.IsAny<VerificationPurpose>(), It.IsAny<VerificationMethod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("token");

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var dispatcher = new Mock<IVerificationDispatcher>();
        dispatcher.Setup(d => d.DispatchAsync(It.IsAny<VerificationDeliveryRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("smtp down"));

        var sut = new VerificationChallengeIssuer(
            repo.Object,
            tokens.Object,
            dispatcher.Object,
            uow.Object,
            Mock.Of<IClock>(c => c.UtcNow == Now),
            Mock.Of<IPersistenceExceptionClassifier>(),
            Options.Create(new PermixaVerificationOptions()));

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.IssueAsync(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            "alice@example.com"));

        Assert.NotNull(added);
        Assert.True(added!.IsInvalidated);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Reissue_InvalidatesPreviousOpen_BeforeInsert()
    {
        var userId = Guid.NewGuid();
        var previous = VerificationChallenge.Create(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "alice@example.com",
            Now.AddMinutes(5),
            createdAtUtc: Now.AddMinutes(-2));

        var repo = new Mock<IVerificationChallengeRepository>();
        repo.Setup(r => r.GetOpenAsync(userId, VerificationPurpose.EmailConfirmation, "alice@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[] { previous });
        repo.Setup(r => r.AddAsync(It.IsAny<VerificationChallenge>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tokens = new Mock<IVerificationTokenProvider>();
        tokens.Setup(t => t.GenerateAsync(It.IsAny<Guid>(), It.IsAny<VerificationPurpose>(), It.IsAny<VerificationMethod>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("new-otp");

        var saveCount = 0;
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Callback(() =>
            {
                saveCount++;
                if (saveCount == 1)
                    Assert.True(previous.IsInvalidated);
            })
            .ReturnsAsync(1);

        var sut = new VerificationChallengeIssuer(
            repo.Object,
            tokens.Object,
            Mock.Of<IVerificationDispatcher>(),
            uow.Object,
            Mock.Of<IClock>(c => c.UtcNow == Now),
            Mock.Of<IPersistenceExceptionClassifier>(),
            Options.Create(new PermixaVerificationOptions
            {
                ResendCooldown = TimeSpan.FromSeconds(60)
            }));

        var result = await sut.IssueAsync(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "alice@example.com");

        Assert.True(result.IsSuccess);
        Assert.True(previous.IsInvalidated);
        Assert.Equal(2, saveCount);
    }
}

public sealed class RequestPasswordResetUseCaseTests
{
    [Fact]
    public async Task UnknownEmail_ReturnsSuccess_WithoutIssuing()
    {
        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.FindUserIdByEmailAsync("missing@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid?)null);

        var tokens = new Mock<IVerificationTokenProvider>(MockBehavior.Strict);
        var issuer = new VerificationChallengeIssuer(
            Mock.Of<IVerificationChallengeRepository>(),
            tokens.Object,
            Mock.Of<IVerificationDispatcher>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(),
            Mock.Of<IPersistenceExceptionClassifier>(),
            Options.Create(new PermixaVerificationOptions()));

        var sut = new RequestPasswordResetUseCase(emails.Object, issuer);
        var result = await sut.ExecuteAsync(new RequestPasswordResetRequest { Email = "missing@example.com" });

        Assert.True(result.IsSuccess);
        tokens.VerifyNoOtherCalls();
    }
}

public sealed class ResetPasswordWithVerificationUseCaseTests
{
    private static readonly DateTime Now = new(2026, 9, 10, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Success_ConsumesChallenge()
    {
        var userId = Guid.NewGuid();
        var challenge = VerificationChallenge.Create(
            userId,
            VerificationPurpose.PasswordReset,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            "alice@example.com",
            Now.AddHours(1),
            createdAtUtc: Now);

        var repo = new Mock<IVerificationChallengeRepository>();
        repo.Setup(r => r.GetByIdAsync(challenge.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("alice@example.com");

        var reset = new Mock<IIdentityPasswordReset>();
        reset.Setup(r => r.ResetPasswordAsync(userId, "token", "NewPassw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityPasswordResetResult.Success());

        var uow = new Mock<IUnitOfWork>();
        var sut = new ResetPasswordWithVerificationUseCase(
            repo.Object,
            emails.Object,
            reset.Object,
            uow.Object,
            Mock.Of<IClock>(c => c.UtcNow == Now));

        var result = await sut.ExecuteAsync(new ResetPasswordWithVerificationRequest
        {
            ChallengeId = challenge.Id,
            Token = "token",
            NewPassword = "NewPassw0rd!"
        });

        Assert.True(result.IsSuccess);
        Assert.True(challenge.IsConsumed);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvalidToken_DoesNotConsume()
    {
        var userId = Guid.NewGuid();
        var challenge = VerificationChallenge.Create(
            userId,
            VerificationPurpose.PasswordReset,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            "alice@example.com",
            Now.AddHours(1),
            createdAtUtc: Now);

        var repo = new Mock<IVerificationChallengeRepository>();
        repo.Setup(r => r.GetByIdAsync(challenge.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync("alice@example.com");

        var reset = new Mock<IIdentityPasswordReset>();
        reset.Setup(r => r.ResetPasswordAsync(userId, "bad", "NewPassw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityPasswordResetResult.FailedInvalidToken());

        var sut = new ResetPasswordWithVerificationUseCase(
            repo.Object,
            emails.Object,
            reset.Object,
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(c => c.UtcNow == Now));

        var result = await sut.ExecuteAsync(new ResetPasswordWithVerificationRequest
        {
            ChallengeId = challenge.Id,
            Token = "bad",
            NewPassword = "NewPassw0rd!"
        });

        Assert.Equal(VerificationErrors.InvalidToken, result.Error);
        Assert.False(challenge.IsConsumed);
    }
}
