using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Mfa;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Register;
using Permixa.Application.Common;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;
using Permixa.Domain.Authentication;
using Microsoft.Extensions.Options;
using Moq;

namespace Permixa.Application.Tests.Authentication;

public sealed class RegisterUserUseCaseTests
{
    [Fact]
    public async Task Register_Success_MapsCreatorResult()
    {
        var creator = new Mock<IIdentityUserCreator>();
        var userId = Guid.NewGuid();
        creator.Setup(c => c.CreateAsync("alice", "alice@example.com", "Passw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityUserCreationResult.Success(userId, emailConfirmed: false));

        var sut = new RegisterUserUseCase(creator.Object);
        var result = await sut.ExecuteAsync(new RegisterRequest
        {
            UserName = " alice ",
            Email = " alice@example.com ",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Equal("alice", result.Value.UserName);
        Assert.Equal("alice@example.com", result.Value.Email);
        Assert.False(result.Value.EmailConfirmed);
    }

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsEmailAlreadyExists()
    {
        var creator = new Mock<IIdentityUserCreator>();
        creator.Setup(c => c.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityUserCreationResult.Failed(IdentityUserCreationFailure.DuplicateEmail));

        var sut = new RegisterUserUseCase(creator.Object);
        var result = await sut.ExecuteAsync(new RegisterRequest
        {
            UserName = "alice",
            Email = "alice@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.EmailAlreadyExists, result.Error);
    }

    [Fact]
    public async Task Register_PropagatesCancellationToken()
    {
        using var cts = new CancellationTokenSource();
        var token = cts.Token;
        var creator = new Mock<IIdentityUserCreator>();
        creator.Setup(c => c.CreateAsync("alice", "alice@example.com", "Passw0rd!", token))
            .ReturnsAsync(IdentityUserCreationResult.Success(Guid.NewGuid(), emailConfirmed: false));

        var sut = new RegisterUserUseCase(creator.Object);
        var result = await sut.ExecuteAsync(new RegisterRequest
        {
            UserName = "alice",
            Email = "alice@example.com",
            Password = "Passw0rd!"
        }, token);

        Assert.True(result.IsSuccess);
        creator.Verify(c => c.CreateAsync("alice", "alice@example.com", "Passw0rd!", token), Times.Once);
    }

    [Fact]
    public async Task Register_DuplicateEmail_DoesNotIssueTokensOrRoles()
    {
        // RegisterUserUseCase only depends on IIdentityUserCreator — failure path must not invent side effects.
        var creator = new Mock<IIdentityUserCreator>(MockBehavior.Strict);
        creator.Setup(c => c.CreateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityUserCreationResult.Failed(IdentityUserCreationFailure.DuplicateEmail));

        var sut = new RegisterUserUseCase(creator.Object);
        var result = await sut.ExecuteAsync(new RegisterRequest
        {
            UserName = "alice",
            Email = "alice@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.EmailAlreadyExists, result.Error);
        creator.VerifyAll();
    }
}

public sealed class LoginUseCaseTests
{
    private static LoginUseCase CreateSut(
        IIdentityAuthenticator authenticator,
        IAuthenticationTokenService tokens,
        PermixaAuthenticationOptions? options = null,
        MfaLoginChallengeIssuer? issuer = null) =>
        new(
            authenticator,
            tokens,
            issuer ?? CreateIssuer().Issuer,
            Options.Create(options ?? new PermixaAuthenticationOptions()));

    private static (MfaLoginChallengeIssuer Issuer, Mock<IMfaLoginChallengeRepository> Challenges) CreateIssuer()
    {
        var challenges = new Mock<IMfaLoginChallengeRepository>();
        challenges.Setup(c => c.AddAsync(It.IsAny<MfaLoginChallenge>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var crypto = new Mock<IRefreshTokenCrypto>();
        crypto.Setup(c => c.GenerateRawToken()).Returns("mfa-proof");
        crypto.Setup(c => c.HashToken("mfa-proof")).Returns("mfa-hash");
        var uow = new Mock<IUnitOfWork>();
        uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((action, ct) => action(ct));
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>())).ReturnsAsync(1);
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(new DateTime(2026, 9, 13, 21, 0, 0, DateTimeKind.Utc));
        var persistence = new Mock<IPersistenceExceptionClassifier>();

        return (
            new MfaLoginChallengeIssuer(
                challenges.Object,
                crypto.Object,
                uow.Object,
                clock.Object,
                persistence.Object,
                Options.Create(new PermixaMfaOptions())),
            challenges);
    }

    [Fact]
    public async Task Login_Success_IssuesTokens()
    {
        var userId = Guid.NewGuid();
        var authenticator = new Mock<IIdentityAuthenticator>();
        var tokens = new Mock<IAuthenticationTokenService>();

        authenticator.Setup(a => a.AuthenticateAsync("alice@example.com", "Passw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityAuthenticationResult.Success(userId, emailConfirmed: false));

        tokens.Setup(t => t.IssueAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.Success(new AuthenticationResult
            {
                UserId = userId,
                AccessToken = "access",
                AccessTokenExpiresAtUtc = DateTime.UtcNow.AddMinutes(15),
                RefreshToken = "refresh",
                RefreshTokenExpiresAtUtc = DateTime.UtcNow.AddDays(7)
            }));

        var sut = CreateSut(authenticator.Object, tokens.Object);
        var result = await sut.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = " alice@example.com ",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsAuthenticated);
        Assert.False(result.Value.IsMfaRequired);
        Assert.Equal("access", result.Value.Authentication!.AccessToken);
        Assert.Equal("refresh", result.Value.Authentication.RefreshToken);
    }

    [Fact]
    public async Task Login_TwoFactorEnabled_ReturnsMfaRequired_WithoutTokens()
    {
        var userId = Guid.NewGuid();
        var authenticator = new Mock<IIdentityAuthenticator>();
        var tokens = new Mock<IAuthenticationTokenService>(MockBehavior.Strict);
        var issued = CreateIssuer();

        authenticator.Setup(a => a.AuthenticateAsync("alice@example.com", "Passw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityAuthenticationResult.Success(userId, emailConfirmed: true, twoFactorEnabled: true));

        var sut = CreateSut(authenticator.Object, tokens.Object, issuer: issued.Issuer);
        var result = await sut.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "alice@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsMfaRequired);
        Assert.False(result.Value.IsAuthenticated);
        Assert.Null(result.Value.Authentication);
        Assert.Equal("mfa-proof", result.Value.Mfa!.MfaProof);
        issued.Challenges.Verify(
            c => c.AddAsync(It.Is<MfaLoginChallenge>(ch => ch.UserId == userId && ch.ProofHash == "mfa-hash"), It.IsAny<CancellationToken>()),
            Times.Once);
        tokens.Verify(t => t.IssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_WrongPassword_DoesNotIssueMfaChallenge()
    {
        var authenticator = new Mock<IIdentityAuthenticator>();
        var tokens = new Mock<IAuthenticationTokenService>();
        var issued = CreateIssuer();
        authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityAuthenticationResult.InvalidCredentials());

        var sut = CreateSut(authenticator.Object, tokens.Object, issuer: issued.Issuer);
        var result = await sut.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "alice@example.com",
            Password = "WrongPass1!"
        });

        Assert.Equal(AuthenticationErrors.InvalidCredentials, result.Error);
        issued.Challenges.Verify(
            c => c.AddAsync(It.IsAny<MfaLoginChallenge>(), It.IsAny<CancellationToken>()),
            Times.Never);
        tokens.Verify(t => t.IssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_RequireConfirmedEmail_Unconfirmed_ReturnsEmailNotConfirmed()
    {
        var userId = Guid.NewGuid();
        var authenticator = new Mock<IIdentityAuthenticator>();
        var tokens = new Mock<IAuthenticationTokenService>();

        authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityAuthenticationResult.Success(userId, emailConfirmed: false));

        var sut = CreateSut(
            authenticator.Object,
            tokens.Object,
            new PermixaAuthenticationOptions { RequireConfirmedEmail = true });

        var result = await sut.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "alice@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.EmailNotConfirmed, result.Error);
        tokens.Verify(t => t.IssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_RequireConfirmedEmail_StillReturnsInvalidCredentials_BeforeConfirmationCheck()
    {
        var authenticator = new Mock<IIdentityAuthenticator>();
        var tokens = new Mock<IAuthenticationTokenService>();
        authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityAuthenticationResult.InvalidCredentials());

        var sut = CreateSut(
            authenticator.Object,
            tokens.Object,
            new PermixaAuthenticationOptions { RequireConfirmedEmail = true });

        var result = await sut.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "missing@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.InvalidCredentials, result.Error);
    }

    [Fact]
    public async Task Login_UnknownOrBadPassword_ReturnsInvalidCredentials()
    {
        var authenticator = new Mock<IIdentityAuthenticator>();
        var tokens = new Mock<IAuthenticationTokenService>();
        authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityAuthenticationResult.InvalidCredentials());

        var sut = CreateSut(authenticator.Object, tokens.Object);
        var result = await sut.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "missing@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.InvalidCredentials, result.Error);
        tokens.Verify(t => t.IssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_BadPassword_ReturnsSameInvalidCredentials_AsUnknown()
    {
        var authenticator = new Mock<IIdentityAuthenticator>();
        var tokens = new Mock<IAuthenticationTokenService>();
        authenticator.Setup(a => a.AuthenticateAsync("alice@example.com", "WrongPass1!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityAuthenticationResult.InvalidCredentials());

        var sut = CreateSut(authenticator.Object, tokens.Object);
        var result = await sut.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "alice@example.com",
            Password = "WrongPass1!"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.InvalidCredentials, result.Error);
        tokens.Verify(t => t.IssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_LockedOut_ReturnsLockedOut()
    {
        var authenticator = new Mock<IIdentityAuthenticator>();
        var tokens = new Mock<IAuthenticationTokenService>();
        authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityAuthenticationResult.LockedOut());

        var sut = CreateSut(authenticator.Object, tokens.Object);
        var result = await sut.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "alice@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.LockedOut, result.Error);
        tokens.Verify(t => t.IssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_Disabled_UsesGenericInvalidCredentials()
    {
        var authenticator = new Mock<IIdentityAuthenticator>();
        var tokens = new Mock<IAuthenticationTokenService>();
        authenticator.Setup(a => a.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityAuthenticationResult.InvalidCredentials());

        var sut = CreateSut(authenticator.Object, tokens.Object);
        var result = await sut.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "disabled@example.com",
            Password = "Passw0rd!"
        });

        Assert.Equal(AuthenticationErrors.InvalidCredentials, result.Error);
        tokens.Verify(t => t.IssueAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}


public sealed class AuthenticationTokenServiceTests
{
    private static readonly DateTime Now = new(2026, 6, 1, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Issue_PersistsHash_NotRawToken()
    {
        var access = new Mock<IAccessTokenGenerator>();
        var crypto = new Mock<IRefreshTokenCrypto>();
        var repo = new Mock<IRefreshTokenRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        access.Setup(a => a.GenerateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedAccessToken("jwt", Now.AddMinutes(15)));
        crypto.Setup(c => c.GenerateRawToken()).Returns("raw-token-value");
        crypto.Setup(c => c.HashToken("raw-token-value")).Returns("hashed-token-value");

        RefreshToken? added = null;
        repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((t, _) => added = t)
            .Returns(Task.CompletedTask);

        var sut = CreateSut(access, crypto, repo, uow, clock);
        var userId = Guid.NewGuid();
        var result = await sut.IssueAsync(userId);

        Assert.True(result.IsSuccess);
        Assert.Equal("raw-token-value", result.Value.RefreshToken);
        Assert.Equal(Now.AddDays(7), result.Value.RefreshTokenExpiresAtUtc);
        Assert.NotNull(added);
        Assert.Equal("hashed-token-value", added!.TokenHash);
        Assert.NotEqual("raw-token-value", added.TokenHash);
        Assert.NotEqual(Guid.Empty, added.FamilyId);
        Assert.Equal(Now.AddDays(7), added.ExpiresAtUtc);
        access.Verify(a => a.GenerateAsync(userId, It.IsAny<CancellationToken>()), Times.Once);
        crypto.Verify(c => c.GenerateRawToken(), Times.Once);
        crypto.Verify(c => c.HashToken("raw-token-value"), Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Rotate_Unknown_ReturnsInvalidRefreshToken()
    {
        var access = new Mock<IAccessTokenGenerator>();
        var crypto = new Mock<IRefreshTokenCrypto>();
        var repo = new Mock<IRefreshTokenRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        crypto.Setup(c => c.HashToken("missing")).Returns("missing-hash");
        repo.Setup(r => r.GetByTokenHashAsync("missing-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync((RefreshToken?)null);

        var sut = CreateSut(access, crypto, repo, uow, clock);
        var result = await sut.RotateAsync("missing");

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.InvalidRefreshToken, result.Error);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        access.Verify(a => a.GenerateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rotate_Expired_ReturnsRefreshTokenExpired()
    {
        var existing = RefreshToken.Create(
            Guid.NewGuid(),
            "old-hash",
            Now.AddMinutes(-1),
            Guid.NewGuid(),
            createdAtUtc: Now.AddDays(-1));

        var access = new Mock<IAccessTokenGenerator>();
        var crypto = new Mock<IRefreshTokenCrypto>();
        var repo = new Mock<IRefreshTokenRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now);

        crypto.Setup(c => c.HashToken("old-raw")).Returns("old-hash");
        repo.Setup(r => r.GetByTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateSut(access, crypto, repo, uow, clock);
        var result = await sut.RotateAsync("old-raw");

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.RefreshTokenExpired, result.Error);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rotate_RevokedWithoutReplacement_ReturnsRefreshTokenRevoked()
    {
        var existing = RefreshToken.Create(Guid.NewGuid(), "old-hash", Now.AddDays(7), Guid.NewGuid(), createdAtUtc: Now);
        existing.Revoke(Now.AddMinutes(1), replacedByTokenId: null);

        var access = new Mock<IAccessTokenGenerator>();
        var crypto = new Mock<IRefreshTokenCrypto>();
        var repo = new Mock<IRefreshTokenRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now.AddHours(1));

        crypto.Setup(c => c.HashToken("old-raw")).Returns("old-hash");
        repo.Setup(r => r.GetByTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var sut = CreateSut(access, crypto, repo, uow, clock);
        var result = await sut.RotateAsync("old-raw");

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.RefreshTokenRevoked, result.Error);
        repo.Verify(r => r.GetActiveByFamilyIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rotate_RevokesOld_LinksReplacement_SameFamily()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var existing = RefreshToken.Create(userId, "old-hash", Now.AddDays(7), familyId, createdAtUtc: Now);

        var access = new Mock<IAccessTokenGenerator>();
        var crypto = new Mock<IRefreshTokenCrypto>();
        var repo = new Mock<IRefreshTokenRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now.AddHours(1));

        access.Setup(a => a.GenerateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedAccessToken("jwt2", Now.AddHours(1).AddMinutes(15)));
        crypto.Setup(c => c.HashToken("old-raw")).Returns("old-hash");
        crypto.Setup(c => c.GenerateRawToken()).Returns("new-raw");
        crypto.Setup(c => c.HashToken("new-raw")).Returns("new-hash");
        repo.Setup(r => r.GetByTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        RefreshToken? replacement = null;
        repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Callback<RefreshToken, CancellationToken>((t, _) => replacement = t)
            .Returns(Task.CompletedTask);

        var users = EnabledUserReader();
        var sut = CreateSut(access, crypto, repo, uow, clock, users);
        var result = await sut.RotateAsync("old-raw");

        Assert.True(result.IsSuccess);
        Assert.True(existing.IsRevoked);
        Assert.True(existing.WasReplaced);
        Assert.NotNull(replacement);
        Assert.Equal(familyId, replacement!.FamilyId);
        Assert.Equal(existing.ReplacedByTokenId, replacement.Id);
        Assert.Equal("new-raw", result.Value.RefreshToken);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetActiveByFamilyIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rotate_ReplacedToken_RevokesSameFamilyOnly_PreservesOtherFamily()
    {
        var userId = Guid.NewGuid();
        var familyId = Guid.NewGuid();
        var otherFamilyId = Guid.NewGuid();
        var replaced = RefreshToken.Create(userId, "old-hash", Now.AddDays(7), familyId, createdAtUtc: Now);
        replaced.Revoke(Now.AddMinutes(1), Guid.NewGuid());

        var otherFamilyActive = RefreshToken.Create(userId, "other-hash", Now.AddDays(7), otherFamilyId, createdAtUtc: Now);

        var access = new Mock<IAccessTokenGenerator>();
        var crypto = new Mock<IRefreshTokenCrypto>();
        var repo = new Mock<IRefreshTokenRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now.AddHours(1));

        crypto.Setup(c => c.HashToken("old-raw")).Returns("old-hash");
        repo.Setup(r => r.GetByTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(replaced);
        repo.Setup(r => r.RevokeFamilyForUserAsync(
                userId, familyId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);

        var sut = CreateSut(access, crypto, repo, uow, clock);
        var result = await sut.RotateAsync("old-raw");

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.RefreshTokenReuseDetected, result.Error);
        Assert.False(otherFamilyActive.IsRevoked);
        repo.Verify(
            r => r.RevokeFamilyForUserAsync(userId, familyId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        repo.Verify(
            r => r.RevokeFamilyForUserAsync(userId, otherFamilyId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repo.Verify(r => r.GetActiveByFamilyIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        access.Verify(a => a.GenerateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rotate_ConcurrencyConflict_ReturnsInvalidRefreshToken_WithoutFamilyRevoke()
    {
        var userId = Guid.NewGuid();
        var existing = RefreshToken.Create(userId, "old-hash", Now.AddDays(7), Guid.NewGuid(), createdAtUtc: Now);

        var access = new Mock<IAccessTokenGenerator>();
        var crypto = new Mock<IRefreshTokenCrypto>();
        var repo = new Mock<IRefreshTokenRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now.AddHours(1));

        access.Setup(a => a.GenerateAsync(userId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GeneratedAccessToken("jwt2", Now.AddHours(1).AddMinutes(15)));
        crypto.Setup(c => c.HashToken("old-raw")).Returns("old-hash");
        crypto.Setup(c => c.GenerateRawToken()).Returns("new-raw");
        crypto.Setup(c => c.HashToken("new-raw")).Returns("new-hash");
        repo.Setup(r => r.GetByTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        repo.Setup(r => r.AddAsync(It.IsAny<RefreshToken>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        uow.Setup(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ConcurrencyConflictException("conflict"));

        var users = EnabledUserReader();
        var sut = CreateSut(access, crypto, repo, uow, clock, users);
        var result = await sut.RotateAsync("old-raw");

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.InvalidRefreshToken, result.Error);
        repo.Verify(r => r.GetActiveByFamilyIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Revoke_WithoutReplacement_DoesNotMarkWasReplaced()
    {
        var existing = RefreshToken.Create(Guid.NewGuid(), "hash", Now.AddDays(7), Guid.NewGuid(), createdAtUtc: Now);

        var access = new Mock<IAccessTokenGenerator>();
        var crypto = new Mock<IRefreshTokenCrypto>();
        var repo = new Mock<IRefreshTokenRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now.AddHours(1));

        crypto.Setup(c => c.HashToken("raw")).Returns("hash");
        repo.Setup(r => r.GetByTokenHashAsync("hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);
        repo.Setup(r => r.RevokeFamilyForUserAsync(
                existing.UserId, existing.FamilyId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1);
        uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((action, ct) => action(ct));

        var sut = CreateSut(access, crypto, repo, uow, clock);
        var result = await sut.RevokeAsync("raw");

        Assert.True(result.IsSuccess);
        repo.Verify(
            r => r.RevokeFamilyForUserAsync(
                existing.UserId, existing.FamilyId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Once);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.GetActiveByFamilyIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rotate_DisabledAccount_ReturnsInvalidRefreshToken_WithoutRotation()
    {
        var userId = Guid.NewGuid();
        var existing = RefreshToken.Create(userId, "old-hash", Now.AddDays(7), Guid.NewGuid(), createdAtUtc: Now);

        var access = new Mock<IAccessTokenGenerator>();
        var crypto = new Mock<IRefreshTokenCrypto>();
        var repo = new Mock<IRefreshTokenRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now.AddHours(1));
        crypto.Setup(c => c.HashToken("old-raw")).Returns("old-hash");
        repo.Setup(r => r.GetByTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var users = new Mock<IIdentityUserReader>();
        users.Setup(u => u.GetAccountStateAsync(userId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAccountState(userId, true, false));

        var sut = CreateSut(access, crypto, repo, uow, clock, users);
        var result = await sut.RotateAsync("old-raw");

        Assert.Equal(AuthenticationErrors.InvalidRefreshToken, result.Error);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
        access.Verify(a => a.GenerateAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Rotate_LockedAccount_ReturnsInvalidRefreshToken_WithoutRotation()
    {
        var userId = Guid.NewGuid();
        var existing = RefreshToken.Create(userId, "old-hash", Now.AddDays(7), Guid.NewGuid(), createdAtUtc: Now);

        var access = new Mock<IAccessTokenGenerator>();
        var crypto = new Mock<IRefreshTokenCrypto>();
        var repo = new Mock<IRefreshTokenRepository>();
        var uow = new Mock<IUnitOfWork>();
        var clock = new Mock<IClock>();
        clock.SetupGet(c => c.UtcNow).Returns(Now.AddHours(1));
        crypto.Setup(c => c.HashToken("old-raw")).Returns("old-hash");
        repo.Setup(r => r.GetByTokenHashAsync("old-hash", It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var users = new Mock<IIdentityUserReader>();
        users.Setup(u => u.GetAccountStateAsync(userId, It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IdentityAccountState(userId, false, true));

        var sut = CreateSut(access, crypto, repo, uow, clock, users);
        var result = await sut.RotateAsync("old-raw");

        Assert.Equal(AuthenticationErrors.InvalidRefreshToken, result.Error);
        uow.Verify(u => u.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    private static Mock<IIdentityUserReader> EnabledUserReader()
    {
        var users = new Mock<IIdentityUserReader>();
        users.Setup(u => u.GetAccountStateAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, DateTime _, CancellationToken _) => new IdentityAccountState(id, false, false));
        return users;
    }

    private static AuthenticationTokenService CreateSut(
        Mock<IAccessTokenGenerator> access,
        Mock<IRefreshTokenCrypto> crypto,
        Mock<IRefreshTokenRepository> repo,
        Mock<IUnitOfWork> uow,
        Mock<IClock> clock,
        Mock<IIdentityUserReader>? users = null) =>
        new(
            access.Object,
            crypto.Object,
            repo.Object,
            (users ?? EnabledUserReader()).Object,
            Mock.Of<Permixa.Application.Audit.Abstractions.IIamAuditSink>(),
            uow.Object,
            clock.Object,
            Options.Create(new PermixaAuthenticationOptions
            {
                RefreshTokenLifetime = TimeSpan.FromDays(7)
            }));
}
