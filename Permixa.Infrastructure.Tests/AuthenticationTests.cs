using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Logout;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authentication.Register;
using Permixa.Application.Identity.Abstractions;
using Permixa.Infrastructure.Authentication;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Permixa.Infrastructure.Tests;

[Collection(SqlServerCollection.Name)]
public sealed class AuthenticationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;
    private ServiceProvider _sp = null!;

    public AuthenticationTests(SqlServerContainerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    public async Task InitializeAsync()
    {
        _connectionString = _sqlServer.CreateUniqueDatabaseConnectionString();
        _sp = BuildProvider();
        using var scope = _sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _sp.DisposeAsync();

    [Fact]
    public async Task Register_Succeeds_WithoutPrivilegedRole_AndEmailUnconfirmed()
    {
        using var scope = _sp.CreateScope();
        var register = scope.ServiceProvider.GetRequiredService<RegisterUserUseCase>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var roleReader = scope.ServiceProvider.GetRequiredService<IIdentityUserReader>();

        var result = await register.ExecuteAsync(new RegisterRequest
        {
            UserName = "alice",
            Email = "alice@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.EmailConfirmed);

        var user = await users.FindByIdAsync(result.Value.UserId.ToString());
        Assert.NotNull(user);
        Assert.True(await users.CheckPasswordAsync(user, "Passw0rd!"));
        Assert.False(user.EmailConfirmed);
        Assert.Empty(await roleReader.GetUserRoleIdsAsync(user.Id));
    }

    [Fact]
    public async Task Register_DuplicateEmail_Fails()
    {
        using var scope = _sp.CreateScope();
        var register = scope.ServiceProvider.GetRequiredService<RegisterUserUseCase>();

        Assert.True((await register.ExecuteAsync(new RegisterRequest
        {
            UserName = "bob",
            Email = "bob@example.com",
            Password = "Passw0rd!"
        })).IsSuccess);

        var duplicate = await register.ExecuteAsync(new RegisterRequest
        {
            UserName = "bob2",
            Email = "bob@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(duplicate.IsFailure);
        Assert.Equal(AuthenticationErrors.EmailAlreadyExists.Code, duplicate.Error!.Code);
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsTokenPair()
    {
        await RegisterAsync("carol", "carol@example.com", "Passw0rd!");

        using var scope = _sp.CreateScope();
        var login = scope.ServiceProvider.GetRequiredService<LoginUseCase>();
        var result = await login.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "carol@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsAuthenticated);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Authentication!.AccessToken));
        Assert.False(string.IsNullOrWhiteSpace(result.Value.Authentication.RefreshToken));
        AssertTokensDoNotContainForbiddenClaims(result.Value.Authentication.AccessToken);
    }

    [Fact]
    public async Task Login_InvalidPassword_ReturnsInvalidCredentials()
    {
        await RegisterAsync("dave", "dave@example.com", "Passw0rd!");

        using var scope = _sp.CreateScope();
        var login = scope.ServiceProvider.GetRequiredService<LoginUseCase>();
        var result = await login.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "dave@example.com",
            Password = "WrongPass1!"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.InvalidCredentials.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Login_UnknownUser_ReturnsInvalidCredentials()
    {
        using var scope = _sp.CreateScope();
        var login = scope.ServiceProvider.GetRequiredService<LoginUseCase>();
        var result = await login.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "missing@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.InvalidCredentials.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Login_LockedOutUser_ReturnsLockedOut()
    {
        await RegisterAsync("erin", "erin@example.com", "Passw0rd!");

        using var scope = _sp.CreateScope();
        var login = scope.ServiceProvider.GetRequiredService<LoginUseCase>();

        for (var i = 0; i < 5; i++)
        {
            var failed = await login.ExecuteAsync(new LoginRequest
            {
                EmailOrUserName = "erin@example.com",
                Password = "WrongPass1!"
            });
            Assert.Equal(AuthenticationErrors.InvalidCredentials.Code, failed.Error!.Code);
        }

        var locked = await login.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = "erin@example.com",
            Password = "Passw0rd!"
        });

        Assert.True(locked.IsFailure);
        Assert.Equal(AuthenticationErrors.LockedOut.Code, locked.Error!.Code);
    }

    [Fact]
    public async Task AccessToken_IsRs256_WithRequiredClaims_Only()
    {
        await RegisterAsync("frank", "frank@example.com", "Passw0rd!");
        var auth = await LoginAsync("frank@example.com", "Passw0rd!");

        var handler = new JwtSecurityTokenHandler();
        using var rsa = RSA.Create();
        rsa.ImportFromPem(TestJwtKeys.PrivateKeyPem);

        var principal = handler.ValidateToken(auth.AccessToken, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = TestJwtKeys.Issuer,
            ValidateAudience = true,
            ValidAudience = TestJwtKeys.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new RsaSecurityKey(rsa),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        }, out var validated);

        Assert.NotNull(principal);
        var jwt = Assert.IsType<JwtSecurityToken>(validated);
        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sub);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Jti);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Iat);
        Assert.True(jwt.ValidTo > DateTime.UnixEpoch);
        AssertTokensDoNotContainForbiddenClaims(auth.AccessToken);

        var expectedExpiry = auth.AccessTokenExpiresAtUtc;
        Assert.True(Math.Abs((jwt.ValidTo.ToUniversalTime() - expectedExpiry).TotalSeconds) < 2);
    }

    [Fact]
    public async Task RefreshToken_PersistsHash_NotRawValue()
    {
        await RegisterAsync("gina", "gina@example.com", "Passw0rd!");
        var auth = await LoginAsync("gina@example.com", "Passw0rd!");

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();

        var stored = await db.RefreshTokens.SingleAsync();
        Assert.NotEqual(auth.RefreshToken, stored.TokenHash);
        Assert.Equal(crypto.HashToken(auth.RefreshToken), stored.TokenHash);
        Assert.Matches("^[0-9a-f]{64}$", stored.TokenHash);
        Assert.DoesNotContain(auth.RefreshToken, stored.TokenHash, StringComparison.Ordinal);
        Assert.NotEqual(Guid.Empty, stored.FamilyId);
    }

    [Fact]
    public async Task Refresh_RotatesToken_AndOldCannotRotateAgain()
    {
        await RegisterAsync("hank", "hank@example.com", "Passw0rd!");
        var auth = await LoginAsync("hank@example.com", "Passw0rd!");

        using var scope = _sp.CreateScope();
        var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rotated = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
        Assert.True(rotated.IsSuccess);
        Assert.NotEqual(auth.RefreshToken, rotated.Value.RefreshToken);

        var old = await db.RefreshTokens.SingleAsync(t => t.TokenHash ==
            scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>().HashToken(auth.RefreshToken));
        Assert.True(old.IsRevoked);
        Assert.NotNull(old.ReplacedByTokenId);

        var reuse = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
        Assert.True(reuse.IsFailure);
        Assert.Equal(AuthenticationErrors.RefreshTokenReuseDetected.Code, reuse.Error!.Code);
    }

    [Fact]
    public async Task Refresh_Reuse_RevokesEntireFamily_PreservesOtherFamilies()
    {
        await RegisterAsync("ivy", "ivy@example.com", "Passw0rd!");
        var deviceA = await LoginAsync("ivy@example.com", "Passw0rd!");
        var deviceB = await LoginAsync("ivy@example.com", "Passw0rd!");

        using var scope = _sp.CreateScope();
        var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
        var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var rotatedA = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = deviceA.RefreshToken });
        Assert.True(rotatedA.IsSuccess);

        var reuse = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = deviceA.RefreshToken });
        Assert.Equal(AuthenticationErrors.RefreshTokenReuseDetected.Code, reuse.Error!.Code);

        var familyAId = (await db.RefreshTokens.FirstAsync(t =>
            t.TokenHash == crypto.HashToken(deviceA.RefreshToken))).FamilyId;

        Assert.Equal(0, await db.RefreshTokens.CountAsync(t =>
            t.FamilyId == familyAId && t.RevokedAtUtc == null));

        var deviceBEntity = await db.RefreshTokens.SingleAsync(t =>
            t.TokenHash == crypto.HashToken(deviceB.RefreshToken));
        Assert.False(deviceBEntity.IsRevoked);

        var stillB = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = deviceB.RefreshToken });
        Assert.True(stillB.IsSuccess);
    }

    [Fact]
    public async Task Refresh_Expired_IsRejected()
    {
        await RegisterAsync("jade", "jade@example.com", "Passw0rd!");
        var auth = await LoginAsync("jade@example.com", "Passw0rd!");

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();
        var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();

        var entity = await db.RefreshTokens.SingleAsync(t =>
            t.TokenHash == crypto.HashToken(auth.RefreshToken));

        // Force expiry via reconstituting isn't possible on tracked private setters —
        // use ExecuteUpdate for test setup only.
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE RefreshTokens SET ExpiresAtUtc = {DateTime.UtcNow.AddMinutes(-1)} WHERE Id = {entity.Id}");

        db.ChangeTracker.Clear();

        var result = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.RefreshTokenExpired.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Login_DisabledUser_ReturnsGenericInvalidCredentials()
    {
        await RegisterAsync("disabled.login", "disabled.login@example.com", "Passw0rd!");

        using (var scope = _sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync("disabled.login@example.com");
            user!.SetDisabled(true);
            Assert.Equal(IdentityResult.Success, await users.UpdateAsync(user));
        }

        using (var scope = _sp.CreateScope())
        {
            var login = scope.ServiceProvider.GetRequiredService<LoginUseCase>();
            var result = await login.ExecuteAsync(new LoginRequest
            {
                EmailOrUserName = "disabled.login@example.com",
                Password = "Passw0rd!"
            });

            Assert.True(result.IsFailure);
            Assert.Equal(AuthenticationErrors.InvalidCredentials.Code, result.Error!.Code);
        }
    }

    [Fact]
    public async Task Refresh_AfterDisableWithoutRevoke_IsRejected_WithoutRotation()
    {
        await RegisterAsync("disabled.refresh", "disabled.refresh@example.com", "Passw0rd!");
        var auth = await LoginAsync("disabled.refresh@example.com", "Passw0rd!");

        using (var scope = _sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync("disabled.refresh@example.com");
            user!.SetDisabled(true);
            Assert.Equal(IdentityResult.Success, await users.UpdateAsync(user));
        }

        using (var scope = _sp.CreateScope())
        {
            var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var crypto = scope.ServiceProvider.GetRequiredService<IRefreshTokenCrypto>();

            var result = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
            Assert.True(result.IsFailure);
            Assert.Equal(AuthenticationErrors.InvalidRefreshToken.Code, result.Error!.Code);

            var stored = await db.RefreshTokens.SingleAsync(t =>
                t.TokenHash == crypto.HashToken(auth.RefreshToken));
            Assert.False(stored.IsRevoked);
        }
    }

    [Fact]
    public async Task Refresh_AfterLock_IsRejected_AndWorksAgainAfterUnlock()
    {
        await RegisterAsync("locked.refresh", "locked.refresh@example.com", "Passw0rd!");
        var auth = await LoginAsync("locked.refresh@example.com", "Passw0rd!");

        using (var scope = _sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync("locked.refresh@example.com");
            Assert.Equal(IdentityResult.Success, await users.SetLockoutEnabledAsync(user!, true));
            Assert.Equal(IdentityResult.Success, await users.SetLockoutEndDateAsync(user!, DateTimeOffset.UtcNow.AddHours(2)));
        }

        using (var scope = _sp.CreateScope())
        {
            var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
            var result = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
            Assert.True(result.IsFailure);
            Assert.Equal(AuthenticationErrors.InvalidRefreshToken.Code, result.Error!.Code);
        }

        using (var scope = _sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync("locked.refresh@example.com");
            Assert.Equal(IdentityResult.Success, await users.SetLockoutEndDateAsync(user!, null));
            Assert.Equal(IdentityResult.Success, await users.ResetAccessFailedCountAsync(user!));
        }

        using (var scope = _sp.CreateScope())
        {
            var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
            var result = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = auth.RefreshToken });
            Assert.True(result.IsSuccess, result.Error?.Description);
        }
    }

    [Fact]
    public async Task AccessToken_RemainsStructurallyValid_AfterDisable()
    {
        // Deliberate security tradeoff: Lock/Disable do not blacklist access JWTs.
        // Already-issued access tokens remain usable until natural expiration.
        await RegisterAsync("jwt.disable", "jwt.disable@example.com", "Passw0rd!");
        var auth = await LoginAsync("jwt.disable@example.com", "Passw0rd!");

        using (var scope = _sp.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = await users.FindByEmailAsync("jwt.disable@example.com");
            user!.SetDisabled(true);
            Assert.Equal(IdentityResult.Success, await users.UpdateAsync(user));
        }

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(auth.AccessToken);
        Assert.Equal("RS256", jwt.Header.Alg);
        Assert.True(jwt.ValidTo > DateTime.UtcNow);
        Assert.Contains(jwt.Claims, c => c.Type == JwtRegisteredClaimNames.Sub);
        AssertTokensDoNotContainForbiddenClaims(auth.AccessToken);
    }

    [Fact]
    public async Task Refresh_UnknownToken_IsRejected()
    {
        using var scope = _sp.CreateScope();
        var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
        var result = await refresh.ExecuteAsync(new RefreshTokenRequest
        {
            RefreshToken = "not-a-real-token"
        });

        Assert.True(result.IsFailure);
        Assert.Equal(AuthenticationErrors.InvalidRefreshToken.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Logout_RevokesCurrentFamily_NotOtherFamilies()
    {
        await RegisterAsync("kate", "kate@example.com", "Passw0rd!");
        var a = await LoginAsync("kate@example.com", "Passw0rd!");
        var b = await LoginAsync("kate@example.com", "Passw0rd!");

        AuthenticationResult rotatedA;
        using (var scope = _sp.CreateScope())
        {
            var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
            var rotated = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = a.RefreshToken });
            Assert.True(rotated.IsSuccess, rotated.Error?.Description);
            rotatedA = rotated.Value;
        }

        using (var scope = _sp.CreateScope())
        {
            var logout = scope.ServiceProvider.GetRequiredService<LogoutUseCase>();
            Assert.True((await logout.ExecuteAsync(new RevokeRefreshTokenRequest
            {
                RefreshToken = rotatedA.RefreshToken
            })).IsSuccess);
        }

        using (var scope = _sp.CreateScope())
        {
            var refresh = scope.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
            var revokedRotated = await refresh.ExecuteAsync(
                new RefreshTokenRequest { RefreshToken = rotatedA.RefreshToken });
            Assert.Equal(AuthenticationErrors.RefreshTokenRevoked.Code, revokedRotated.Error!.Code);

            var revokedOriginal = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = a.RefreshToken });
            Assert.True(revokedOriginal.IsFailure);

            var stillB = await refresh.ExecuteAsync(new RefreshTokenRequest { RefreshToken = b.RefreshToken });
            Assert.True(stillB.IsSuccess, stillB.Error?.Description);
        }
    }

    [Fact]
    public async Task ConcurrentRefresh_OnlyOneSucceeds()
    {
        await RegisterAsync("leo", "leo@example.com", "Passw0rd!");
        var auth = await LoginAsync("leo@example.com", "Passw0rd!");

        await using var scope1 = _sp.CreateAsyncScope();
        await using var scope2 = _sp.CreateAsyncScope();
        var refresh1 = scope1.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();
        var refresh2 = scope2.ServiceProvider.GetRequiredService<RefreshAccessTokenUseCase>();

        var request = new RefreshTokenRequest { RefreshToken = auth.RefreshToken };
        var t1 = refresh1.ExecuteAsync(request);
        var t2 = refresh2.ExecuteAsync(request);
        await Task.WhenAll(t1, t2);

        var successes = new[] { t1.Result, t2.Result }.Count(r => r.IsSuccess);
        Assert.Equal(1, successes);

        var failures = new[] { t1.Result, t2.Result }.Where(r => r.IsFailure).ToArray();
        Assert.Single(failures);
        Assert.Equal(AuthenticationErrors.InvalidRefreshToken.Code, failures[0].Error!.Code);
    }

    private ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPermixaInfrastructure(o =>
        {
            o.ConnectionString = _connectionString;
        });
        services.AddPermixaAuthentication(o =>
        {
            o.Jwt.Issuer = TestJwtKeys.Issuer;
            o.Jwt.Audience = TestJwtKeys.Audience;
            o.Jwt.PrivateKeyPem = TestJwtKeys.PrivateKeyPem;
            o.Jwt.AccessTokenLifetime = TimeSpan.FromMinutes(15);
            o.Authentication.RefreshTokenLifetime = TimeSpan.FromDays(7);
        });
        return services.BuildServiceProvider();
    }

    private async Task RegisterAsync(string userName, string email, string password)
    {
        using var scope = _sp.CreateScope();
        var register = scope.ServiceProvider.GetRequiredService<RegisterUserUseCase>();
        var result = await register.ExecuteAsync(new RegisterRequest
        {
            UserName = userName,
            Email = email,
            Password = password
        });
        Assert.True(result.IsSuccess, result.Error?.Description);
    }

    private async Task<AuthenticationResult> LoginAsync(string emailOrUserName, string password)
    {
        using var scope = _sp.CreateScope();
        var login = scope.ServiceProvider.GetRequiredService<LoginUseCase>();
        var result = await login.ExecuteAsync(new LoginRequest
        {
            EmailOrUserName = emailOrUserName,
            Password = password
        });
        Assert.True(result.IsSuccess, result.Error?.Description);
        Assert.True(result.Value.IsAuthenticated);
        return result.Value.Authentication!;
    }

    private static void AssertTokensDoNotContainForbiddenClaims(string accessToken)
    {
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(accessToken);
        var types = jwt.Claims.Select(c => c.Type).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("permissions", types);
        Assert.DoesNotContain("RoleLevel", types);
        Assert.DoesNotContain("role", types);
        Assert.DoesNotContain(ClaimTypes.Role, types);
        Assert.DoesNotContain(JwtRegisteredClaimNames.Email, types);
        Assert.DoesNotContain(JwtRegisteredClaimNames.UniqueName, types);
        Assert.DoesNotContain("AuthorizationVersion", types);
        Assert.DoesNotContain("SecurityStamp", types);
    }
}
