using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Application.Verification;
using Permixa.Application.Verification.Abstractions;
using Permixa.Application.Verification.EmailChange;
using Permixa.Domain.Verification;
using Moq;

namespace Permixa.Application.Tests.Verification;

public sealed class RequestEmailChangeUseCaseTests
{
    [Fact]
    public async Task WrongPassword_DoesNotRequestPendingChange()
    {
        var userId = Guid.NewGuid();
        var passwords = new Mock<IIdentityPasswordChange>();
        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailProfileAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityEmailProfile(userId, "old@example.com", "OLD@EXAMPLE.COM", null, true));
        passwords.Setup(p => p.CheckPasswordAsync(userId, "wrong", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var writer = new Mock<IIdentityUserWriter>(MockBehavior.Strict);
        var pending = new PendingEmailChangeService(
            emails.Object,
            writer.Object,
            Mock.Of<IVerificationChallengeRepository>(),
            new VerificationChallengeIssuer(
                Mock.Of<IVerificationChallengeRepository>(),
                Mock.Of<IVerificationTokenProvider>(),
                Mock.Of<IVerificationDispatcher>(),
                Mock.Of<IUnitOfWork>(),
                Mock.Of<IClock>(),
                Mock.Of<IPersistenceExceptionClassifier>(),
                Microsoft.Extensions.Options.Options.Create(new PermixaVerificationOptions())),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>());

        var result = await new RequestEmailChangeUseCase(
                passwords.Object,
                emails.Object,
                pending,
                Mock.Of<IIamAuditSink>(),
                Mock.Of<IUnitOfWork>(),
                Mock.Of<IClock>())
            .ExecuteAsync(new RequestEmailChangeRequest
            {
                UserId = userId,
                CurrentPassword = "wrong",
                NewEmail = "new@example.com"
            });

        Assert.Equal(AuthenticationErrors.CurrentPasswordInvalid, result.Error);
        writer.Verify(
            w => w.SetPendingEmailAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

public sealed class PendingEmailChangeServiceTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 20, 0, 0, DateTimeKind.Utc);
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task SameAsCurrentEmail_IsRejected()
    {
        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailProfileAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityEmailProfile(_userId, "old@example.com", "OLD@EXAMPLE.COM", null, true));

        var writer = new Mock<IIdentityUserWriter>(MockBehavior.Strict);
        var challenges = new Mock<IVerificationChallengeRepository>(MockBehavior.Strict);
        var issuer = CreateIssuer();

        var sut = new PendingEmailChangeService(
            emails.Object,
            writer.Object,
            challenges.Object,
            issuer,
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(c => c.UtcNow == Now));

        var result = await sut.RequestAsync(_userId, "old@example.com");

        Assert.Equal(AuthenticationErrors.EmailUnchanged, result.Error);
        writer.Verify(
            w => w.SetPendingEmailAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvalidEmail_IsValidationFailure()
    {
        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailProfileAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityEmailProfile(_userId, "old@example.com", "OLD@EXAMPLE.COM", null, true));

        var sut = new PendingEmailChangeService(
            emails.Object,
            Mock.Of<IIdentityUserWriter>(),
            Mock.Of<IVerificationChallengeRepository>(),
            CreateIssuer(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(c => c.UtcNow == Now));

        var result = await sut.RequestAsync(_userId, "not-an-email");
        Assert.Equal(AuthenticationErrors.InvalidEmail, result.Error);
    }

    [Fact]
    public async Task ClaimedEmail_IsConflict()
    {
        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailProfileAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityEmailProfile(_userId, "old@example.com", "OLD@EXAMPLE.COM", null, true));
        emails.Setup(e => e.IsEmailClaimedByAnotherUserAsync(_userId, "taken@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var writer = new Mock<IIdentityUserWriter>(MockBehavior.Strict);
        var sut = new PendingEmailChangeService(
            emails.Object,
            writer.Object,
            Mock.Of<IVerificationChallengeRepository>(),
            CreateIssuer(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(c => c.UtcNow == Now));

        var result = await sut.RequestAsync(_userId, "taken@example.com");
        Assert.Equal(AuthenticationErrors.EmailAlreadyExists, result.Error);
        writer.Verify(
            w => w.SetPendingEmailAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task NewEmail_SetsPending_InvalidatesOpenChallenges_AndIssues()
    {
        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailProfileAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityEmailProfile(_userId, "old@example.com", "OLD@EXAMPLE.COM", "a@example.com", true));
        emails.Setup(e => e.IsEmailClaimedByAnotherUserAsync(_userId, "b@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var writer = new Mock<IIdentityUserWriter>();
        var challenges = new Mock<IVerificationChallengeRepository>();
        challenges.Setup(c => c.GetOpenAsync(_userId, VerificationPurpose.EmailChange, "b@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<VerificationChallenge>());
        challenges.Setup(c => c.AddAsync(It.IsAny<VerificationChallenge>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var tokens = new Mock<IVerificationTokenProvider>();
        tokens.Setup(t => t.GenerateAsync(_userId, VerificationPurpose.EmailChange, VerificationMethod.UrlToken, It.IsAny<CancellationToken>()))
            .ReturnsAsync("token");

        var issuer = new VerificationChallengeIssuer(
            challenges.Object,
            tokens.Object,
            Mock.Of<IVerificationDispatcher>(),
            Mock.Of<IUnitOfWork>(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()) == Task.FromResult(1)),
            Mock.Of<IClock>(c => c.UtcNow == Now),
            Mock.Of<IPersistenceExceptionClassifier>(),
            Microsoft.Extensions.Options.Options.Create(new PermixaVerificationOptions()));

        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var sut = new PendingEmailChangeService(
            emails.Object,
            writer.Object,
            challenges.Object,
            issuer,
            uow.Object,
            Mock.Of<IClock>(c => c.UtcNow == Now));

        var result = await sut.RequestAsync(_userId, "b@example.com");

        Assert.True(result.IsSuccess, result.Error?.Description);
        writer.Verify(w => w.SetPendingEmailAsync(_userId, "b@example.com", It.IsAny<CancellationToken>()), Times.Once);
        challenges.Verify(
            c => c.InvalidateOpenForPurposeAsync(_userId, VerificationPurpose.EmailChange, Now, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static VerificationChallengeIssuer CreateIssuer() =>
        new(
            Mock.Of<IVerificationChallengeRepository>(),
            Mock.Of<IVerificationTokenProvider>(),
            Mock.Of<IVerificationDispatcher>(),
            Mock.Of<IUnitOfWork>(),
            Mock.Of<IClock>(),
            Mock.Of<IPersistenceExceptionClassifier>(),
            Microsoft.Extensions.Options.Options.Create(new PermixaVerificationOptions()));
}

public sealed class ConfirmEmailChangeUseCaseTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task StaleDestination_DoesNotMutateEmail()
    {
        var userId = Guid.NewGuid();
        var challenge = VerificationChallenge.Create(
            userId,
            VerificationPurpose.EmailChange,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            "a@example.com",
            Now.AddHours(1),
            createdAtUtc: Now);

        var challenges = new Mock<IVerificationChallengeRepository>();
        challenges.Setup(c => c.GetByIdAsync(challenge.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailProfileAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityEmailProfile(userId, "old@example.com", "OLD@EXAMPLE.COM", "b@example.com", true));

        var change = new Mock<IIdentityEmailChange>(MockBehavior.Strict);
        var writer = new Mock<IIdentityUserWriter>(MockBehavior.Strict);
        var uow = new Mock<IUnitOfWork>();

        var sut = new ConfirmEmailChangeUseCase(
            challenges.Object,
            emails.Object,
            change.Object,
            writer.Object,
            Mock.Of<IIamAuditSink>(),
            uow.Object,
            Mock.Of<IClock>(c => c.UtcNow == Now));

        var result = await sut.ExecuteAsync(new ConfirmEmailChangeRequest
        {
            ChallengeId = challenge.Id,
            Token = "token"
        });

        Assert.Equal(VerificationErrors.DestinationMismatch, result.Error);
        change.Verify(
            c => c.ChangeEmailAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
        writer.Verify(
            w => w.SetPendingEmailAsync(It.IsAny<Guid>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ValidChallenge_ChangesEmail_ClearsPending_AndConsumes()
    {
        var userId = Guid.NewGuid();
        var challenge = VerificationChallenge.Create(
            userId,
            VerificationPurpose.EmailChange,
            VerificationMethod.UrlToken,
            VerificationChannel.Email,
            "new@example.com",
            Now.AddHours(1),
            createdAtUtc: Now);

        var challenges = new Mock<IVerificationChallengeRepository>();
        challenges.Setup(c => c.GetByIdAsync(challenge.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);

        var emails = new Mock<IIdentityUserEmailReader>();
        emails.Setup(e => e.GetEmailProfileAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityEmailProfile(userId, "old@example.com", "OLD@EXAMPLE.COM", "new@example.com", true));
        emails.Setup(e => e.IsNormalizedEmailTakenByAnotherUserAsync(userId, "new@example.com", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var change = new Mock<IIdentityEmailChange>();
        change.Setup(c => c.ChangeEmailAsync(userId, "new@example.com", "token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityEmailChangeResult.Success());

        var writer = new Mock<IIdentityUserWriter>();
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((action, ct) => action(ct));
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);

        var sut = new ConfirmEmailChangeUseCase(
            challenges.Object,
            emails.Object,
            change.Object,
            writer.Object,
            Mock.Of<IIamAuditSink>(),
            uow.Object,
            Mock.Of<IClock>(c => c.UtcNow == Now));

        var result = await sut.ExecuteAsync(new ConfirmEmailChangeRequest
        {
            ChallengeId = challenge.Id,
            Token = "token"
        });

        Assert.True(result.IsSuccess);
        Assert.True(challenge.IsConsumed);
        writer.Verify(w => w.SetPendingEmailAsync(userId, null, It.IsAny<CancellationToken>()), Times.Once);
    }
}
