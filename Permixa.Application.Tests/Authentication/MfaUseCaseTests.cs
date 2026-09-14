using Permixa.Application.Authentication;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Mfa;
using Permixa.Application.Authentication.Mfa.BeginSetup;
using Permixa.Application.Authentication.Mfa.Complete;
using Permixa.Application.Authentication.Mfa.Disable;
using Permixa.Application.Authentication.Mfa.Enable;
using Permixa.Application.Authentication.Mfa.GetMfaStatus;
using Permixa.Application.Authentication.Mfa.Regenerate;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authorization;
using Permixa.Application.Common;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authentication;
using Microsoft.Extensions.Options;
using Moq;

namespace Permixa.Application.Tests.Authentication;

public sealed class MfaUseCaseTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 21, 0, 0, DateTimeKind.Utc);
    private readonly Guid _userId = Guid.NewGuid();

    [Fact]
    public async Task Status_MapsSnapshot_WithoutCodes()
    {
        var mfa = new Mock<IIdentityMfa>();
        mfa.Setup(m => m.GetStatusAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaStatusSnapshot(true, true, 7));

        var result = await new GetMfaStatusUseCase(mfa.Object)
            .ExecuteAsync(new GetMfaStatusQuery(_userId));

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsEnabled);
        Assert.True(result.Value.HasAuthenticatorKey);
        Assert.Equal(7, result.Value.RecoveryCodesRemaining);
    }

    [Fact]
    public async Task BeginSetup_UnknownUser_ReturnsUserNotFound()
    {
        var mfa = new Mock<IIdentityMfa>();
        mfa.Setup(m => m.BeginSetupAsync(_userId, "Permixa", It.IsAny<CancellationToken>()))
            .ReturnsAsync((AuthenticatorSetupMaterial?)null);

        var result = await new BeginAuthenticatorSetupUseCase(mfa.Object, Options.Create(new PermixaMfaOptions()))
            .ExecuteAsync(new BeginAuthenticatorSetupCommand(_userId));

        Assert.Equal(AuthorizationErrors.UserNotFound, result.Error);
    }

    [Fact]
    public async Task Enable_AlreadyEnabled_DoesNotRevoke()
    {
        var mfa = new Mock<IIdentityMfa>();
        var refresh = new Mock<IRefreshTokenRepository>(MockBehavior.Strict);
        mfa.Setup(m => m.GetStatusAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaStatusSnapshot(true, true, 10));

        var result = await EnableSut(mfa, refresh).ExecuteAsync(new EnableAuthenticatorMfaCommand(_userId, "123456"));

        Assert.Equal(MfaErrors.AlreadyEnabled, result.Error);
        refresh.Verify(
            r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Enable_InvalidTotp_DoesNotEnable()
    {
        var mfa = new Mock<IIdentityMfa>();
        var refresh = new Mock<IRefreshTokenRepository>(MockBehavior.Strict);
        mfa.Setup(m => m.GetStatusAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaStatusSnapshot(false, true, 0));
        mfa.Setup(m => m.VerifyAuthenticatorCodeAsync(_userId, "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await EnableSut(mfa, refresh).ExecuteAsync(new EnableAuthenticatorMfaCommand(_userId, "123 456"));

        Assert.Equal(MfaErrors.CodeInvalid, result.Error);
        mfa.Verify(m => m.EnableAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Enable_Success_RevokesSessions_AndReturnsCodesOnce()
    {
        var mfa = new Mock<IIdentityMfa>();
        var refresh = new Mock<IRefreshTokenRepository>();
        mfa.Setup(m => m.GetStatusAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaStatusSnapshot(false, true, 0));
        mfa.Setup(m => m.VerifyAuthenticatorCodeAsync(_userId, "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        mfa.Setup(m => m.EnableAsync(_userId, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityMfaEnableResult.Success(new[] { "aaaa-bbbb" }));

        var result = await EnableSut(mfa, refresh).ExecuteAsync(new EnableAuthenticatorMfaCommand(_userId, "123456"));

        Assert.True(result.IsSuccess);
        Assert.Equal(new[] { "aaaa-bbbb" }, result.Value.RecoveryCodes);
        refresh.Verify(r => r.RevokeAllForUserAsync(_userId, Now, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Disable_WrongPassword_DoesNotDisable()
    {
        var mfa = new Mock<IIdentityMfa>();
        var passwords = new Mock<IIdentityPasswordChange>();
        var refresh = new Mock<IRefreshTokenRepository>(MockBehavior.Strict);
        mfa.Setup(m => m.GetStatusAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaStatusSnapshot(true, true, 10));
        passwords.Setup(p => p.CheckPasswordAsync(_userId, "wrong", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await DisableSut(mfa, passwords, refresh)
            .ExecuteAsync(new DisableMfaCommand(_userId, "wrong"));

        Assert.Equal(AuthenticationErrors.CurrentPasswordInvalid, result.Error);
        mfa.Verify(m => m.DisableAndResetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Regenerate_NotEnabled_DoesNotGenerate()
    {
        var mfa = new Mock<IIdentityMfa>();
        var passwords = new Mock<IIdentityPasswordChange>();
        mfa.Setup(m => m.GetStatusAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MfaStatusSnapshot(false, true, 0));

        var result = await new RegenerateRecoveryCodesUseCase(
                mfa.Object,
                passwords.Object,
                Mock.Of<IIamAuditSink>(),
                ImmediateUow(),
                Clock(),
                Options.Create(new PermixaMfaOptions()))
            .ExecuteAsync(new RegenerateRecoveryCodesCommand(_userId, "Passw0rd!"));

        Assert.Equal(MfaErrors.NotEnabled, result.Error);
        passwords.Verify(
            p => p.CheckPasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task CompleteTotp_InvalidCode_IncrementsAttempts()
    {
        var fixture = CreateCompletion();
        fixture.Crypto.Setup(c => c.HashToken("proof")).Returns("hash");
        var challenge = MfaLoginChallenge.Create(_userId, "hash", Now.AddMinutes(5), createdAtUtc: Now);
        fixture.Challenges.Setup(c => c.GetByProofHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);
        fixture.Users.Setup(u => u.GetMfaLoginGateAsync(_userId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityMfaLoginGate(_userId, false, false, true, true));
        fixture.Mfa.Setup(m => m.VerifyAuthenticatorCodeAsync(_userId, "000000", It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        fixture.Challenges.Setup(c => c.IncrementAttemptsAsync(challenge.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var result = await new CompleteMfaWithTotpUseCase(fixture.Completion, fixture.Mfa.Object)
            .ExecuteAsync(new CompleteMfaWithTotpRequest { MfaProof = "proof", TotpCode = "000000" });

        Assert.Equal(MfaErrors.CodeInvalid, result.Error);
        fixture.Tokens.Verify(t => t.IssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        fixture.Challenges.Verify(c => c.IncrementAttemptsAsync(challenge.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CompleteTotp_Success_ConsumesAndIssues()
    {
        var fixture = CreateCompletion();
        fixture.Crypto.Setup(c => c.HashToken("proof")).Returns("hash");
        var challenge = MfaLoginChallenge.Create(_userId, "hash", Now.AddMinutes(5), createdAtUtc: Now);
        fixture.Challenges.Setup(c => c.GetByProofHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);
        fixture.Users.Setup(u => u.GetMfaLoginGateAsync(_userId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityMfaLoginGate(_userId, false, false, true, true));
        fixture.Mfa.Setup(m => m.VerifyAuthenticatorCodeAsync(_userId, "123456", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        fixture.Challenges.Setup(c => c.ConsumeUnconsumedAsync(challenge.Id, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        fixture.Tokens.Setup(t => t.IssueAsync(_userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AuthenticationResult
            {
                UserId = _userId,
                AccessToken = "access",
                AccessTokenExpiresAtUtc = Now.AddMinutes(15),
                RefreshToken = "refresh",
                RefreshTokenExpiresAtUtc = Now.AddDays(7)
            }));

        var result = await new CompleteMfaWithTotpUseCase(fixture.Completion, fixture.Mfa.Object)
            .ExecuteAsync(new CompleteMfaWithTotpRequest { MfaProof = "proof", TotpCode = "123456" });

        Assert.True(result.IsSuccess);
        Assert.Equal("access", result.Value.AccessToken);
        Assert.Equal("refresh", result.Value.RefreshToken);
    }

    [Fact]
    public async Task CompleteTotp_MfaDisabledMidChallenge_ReturnsNotEnabled()
    {
        var fixture = CreateCompletion();
        fixture.Crypto.Setup(c => c.HashToken("proof")).Returns("hash");
        var challenge = MfaLoginChallenge.Create(_userId, "hash", Now.AddMinutes(5), createdAtUtc: Now);
        fixture.Challenges.Setup(c => c.GetByProofHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(challenge);
        fixture.Users.Setup(u => u.GetMfaLoginGateAsync(_userId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityMfaLoginGate(_userId, false, false, true, false));

        var result = await new CompleteMfaWithTotpUseCase(fixture.Completion, fixture.Mfa.Object)
            .ExecuteAsync(new CompleteMfaWithTotpRequest { MfaProof = "proof", TotpCode = "123456" });

        Assert.Equal(MfaErrors.NotEnabled, result.Error);
        fixture.Mfa.Verify(
            m => m.VerifyAuthenticatorCodeAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Issuer_UniqueConflict_RetriesAsReplace()
    {
        var existing = MfaLoginChallenge.Create(_userId, "old-hash", Now.AddMinutes(5), createdAtUtc: Now);
        var lookups = 0;
        var challenges = new Mock<IMfaLoginChallengeRepository>();
        challenges.Setup(c => c.GetByUserIdAsync(_userId, It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(lookups++ == 0 ? null : existing));
        challenges.Setup(c => c.AddAsync(It.IsAny<MfaLoginChallenge>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var crypto = new Mock<IRefreshTokenCrypto>();
        crypto.Setup(c => c.GenerateRawToken()).Returns("raw-proof");
        crypto.Setup(c => c.HashToken("raw-proof")).Returns("new-hash");

        var saves = 0;
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((action, ct) => action(ct));
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                if (saves++ == 0)
                    throw new InvalidOperationException("unique");
                return Task.FromResult(1);
            });

        var persistence = new Mock<IPersistenceExceptionClassifier>();
        persistence.Setup(p => p.IsUniqueConstraintViolation(It.IsAny<Exception>())).Returns(true);
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        var issuer = new MfaLoginChallengeIssuer(
            challenges.Object,
            crypto.Object,
            uow.Object,
            clock.Object,
            persistence.Object,
            Options.Create(new PermixaMfaOptions()));

        var issued = await issuer.IssueAsync(_userId);

        Assert.Equal("raw-proof", issued.Proof);
        Assert.Equal("new-hash", existing.ProofHash);
        Assert.Equal(2, saves);
    }

    private EnableAuthenticatorMfaUseCase EnableSut(
        Mock<IIdentityMfa> mfa,
        Mock<IRefreshTokenRepository> refresh) =>
        new(mfa.Object, refresh.Object, Mock.Of<IIamAuditSink>(), ImmediateUow(), Clock(), Options.Create(new PermixaMfaOptions()));

    private DisableMfaUseCase DisableSut(
        Mock<IIdentityMfa> mfa,
        Mock<IIdentityPasswordChange> passwords,
        Mock<IRefreshTokenRepository> refresh) =>
        new(mfa.Object, passwords.Object, refresh.Object, Mock.Of<IIamAuditSink>(), ImmediateUow(), Clock());

    private CompletionFixture CreateCompletion()
    {
        var challenges = new Mock<IMfaLoginChallengeRepository>();
        var crypto = new Mock<IRefreshTokenCrypto>();
        var users = new Mock<IIdentityUserReader>();
        var mfa = new Mock<IIdentityMfa>();
        var tokens = new Mock<IAuthenticationTokenService>();
        var completion = new MfaLoginCompletion(
            challenges.Object,
            crypto.Object,
            users.Object,
            mfa.Object,
            tokens.Object,
            ImmediateUow(),
            Clock(),
            Options.Create(new PermixaAuthenticationOptions()),
            Options.Create(new PermixaMfaOptions { MaxAttempts = 5 }));
        return new CompletionFixture(completion, challenges, crypto, users, mfa, tokens);
    }

    private static IUnitOfWork ImmediateUow()
    {
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((action, ct) => action(ct));
        return uow.Object;
    }

    private IClock Clock()
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);
        return clock.Object;
    }

    private sealed record CompletionFixture(
        MfaLoginCompletion Completion,
        Mock<IMfaLoginChallengeRepository> Challenges,
        Mock<IRefreshTokenCrypto> Crypto,
        Mock<IIdentityUserReader> Users,
        Mock<IIdentityMfa> Mfa,
        Mock<IAuthenticationTokenService> Tokens);
}
