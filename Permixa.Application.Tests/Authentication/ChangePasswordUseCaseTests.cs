using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.ChangePassword;
using Permixa.Application.Authorization;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Moq;

namespace Permixa.Application.Tests.Authentication;

public sealed class ChangePasswordUseCaseTests
{
    private static readonly DateTime Now = new(2026, 9, 13, 20, 0, 0, DateTimeKind.Utc);

    private readonly Mock<IIdentityPasswordChange> _passwords = new();
    private readonly Mock<IRefreshTokenRepository> _refresh = new();
    private readonly Mock<IIamAuditSink> _audit = new();
    private readonly Mock<IUnitOfWork> _uow = new();
    private readonly Mock<IClock> _clock = new();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _familyId = Guid.NewGuid();

    public ChangePasswordUseCaseTests()
    {
        _clock.SetupGet(c => c.UtcNow).Returns(Now);
        _uow.Setup(u => u.ExecuteInTransactionAsync(It.IsAny<Func<CancellationToken, Task>>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task>, CancellationToken>((action, ct) => action(ct));
    }

    [Fact]
    public async Task CorrectPassword_WithCurrentFamily_PreservesFamily_AndRevokesOthers()
    {
        _passwords.Setup(p => p.ChangePasswordAsync(_userId, "old", "N3wPassw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityPasswordChangeResult.Success());
        _refresh.Setup(r => r.HasActiveFamilyForUserAsync(_userId, _familyId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await Sut().ExecuteAsync(new ChangePasswordRequest
        {
            UserId = _userId,
            CurrentPassword = "old",
            NewPassword = "N3wPassw0rd!",
            CurrentFamilyId = _familyId
        });

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.ReauthenticationRequired);
        _refresh.Verify(r => r.RevokeAllForUserExceptFamilyAsync(_userId, _familyId, Now, It.IsAny<CancellationToken>()), Times.Once);
        _refresh.Verify(r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task MissingCurrentFamily_RevokesAll()
    {
        _passwords.Setup(p => p.ChangePasswordAsync(_userId, "old", "N3wPassw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityPasswordChangeResult.Success());

        var result = await Sut().ExecuteAsync(new ChangePasswordRequest
        {
            UserId = _userId,
            CurrentPassword = "old",
            NewPassword = "N3wPassw0rd!"
        });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ReauthenticationRequired);
        _refresh.Verify(r => r.RevokeAllForUserAsync(_userId, Now, It.IsAny<CancellationToken>()), Times.Once);
        _refresh.Verify(
            r => r.RevokeAllForUserExceptFamilyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ForeignCurrentFamily_RevokesAll()
    {
        _passwords.Setup(p => p.ChangePasswordAsync(_userId, "old", "N3wPassw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityPasswordChangeResult.Success());
        _refresh.Setup(r => r.HasActiveFamilyForUserAsync(_userId, _familyId, Now, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await Sut().ExecuteAsync(new ChangePasswordRequest
        {
            UserId = _userId,
            CurrentPassword = "old",
            NewPassword = "N3wPassw0rd!",
            CurrentFamilyId = _familyId
        });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.ReauthenticationRequired);
        _refresh.Verify(r => r.RevokeAllForUserAsync(_userId, Now, It.IsAny<CancellationToken>()), Times.Once);
        _refresh.Verify(
            r => r.RevokeAllForUserExceptFamilyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task WrongCurrentPassword_DoesNotRevoke()
    {
        _passwords.Setup(p => p.ChangePasswordAsync(_userId, "wrong", "N3wPassw0rd!", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityPasswordChangeResult.FailedCurrentPassword());

        var result = await Sut().ExecuteAsync(new ChangePasswordRequest
        {
            UserId = _userId,
            CurrentPassword = "wrong",
            NewPassword = "N3wPassw0rd!",
            CurrentFamilyId = _familyId
        });

        Assert.Equal(AuthenticationErrors.CurrentPasswordInvalid, result.Error);
        _refresh.Verify(
            r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _refresh.Verify(
            r => r.RevokeAllForUserExceptFamilyAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task InvalidNewPassword_DoesNotRevoke()
    {
        _passwords.Setup(p => p.ChangePasswordAsync(_userId, "old", "x", It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdentityPasswordChangeResult.FailedNewPassword());

        var result = await Sut().ExecuteAsync(new ChangePasswordRequest
        {
            UserId = _userId,
            CurrentPassword = "old",
            NewPassword = "x"
        });

        Assert.Equal(AuthenticationErrors.InvalidPassword, result.Error);
        _refresh.Verify(
            r => r.RevokeAllForUserAsync(It.IsAny<Guid>(), It.IsAny<DateTime>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task MissingUser_DoesNotRevoke()
    {
        var result = await Sut().ExecuteAsync(new ChangePasswordRequest
        {
            UserId = Guid.Empty,
            CurrentPassword = "old",
            NewPassword = "N3wPassw0rd!"
        });

        Assert.Equal(AuthorizationErrors.UserNotFound, result.Error);
        _passwords.Verify(
            p => p.ChangePasswordAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private ChangePasswordUseCase Sut() =>
        new(_passwords.Object, _refresh.Object, _audit.Object, _uow.Object, _clock.Object);
}
