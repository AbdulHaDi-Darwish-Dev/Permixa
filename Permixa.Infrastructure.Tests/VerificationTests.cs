using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Models;
using Permixa.Application.Authentication.Register;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Verification;
using Permixa.Application.Verification.Abstractions;
using Permixa.Application.Verification.EmailConfirmation;
using Permixa.Application.Verification.Models;
using Permixa.Application.Verification.PasswordReset;
using Permixa.Domain.Verification;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Permixa.Infrastructure.Tests;

internal sealed class CapturingVerificationDispatcher : IVerificationDispatcher
{
    public List<VerificationDeliveryRequest> Deliveries { get; } = new();

    public Func<VerificationDeliveryRequest, Task>? OnDispatch { get; set; }

    public async Task DispatchAsync(
        VerificationDeliveryRequest request,
        CancellationToken cancellationToken = default)
    {
        if (OnDispatch is not null)
            await OnDispatch(request);

        Deliveries.Add(request);
    }
}

[Collection(SqlServerCollection.Name)]
public sealed class VerificationTests : IAsyncLifetime
{
    private readonly SqlServerContainerFixture _sqlServer;
    private string _connectionString = null!;
    private ServiceProvider _sp = null!;
    private CapturingVerificationDispatcher _dispatcher = null!;

    public VerificationTests(SqlServerContainerFixture sqlServer)
    {
        _sqlServer = sqlServer;
    }

    public async Task InitializeAsync()
    {
        _connectionString = _sqlServer.CreateUniqueDatabaseConnectionString();
        _dispatcher = new CapturingVerificationDispatcher();
        _sp = BuildProvider();
        using var scope = _sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _sp.DisposeAsync();

    [Fact]
    public async Task EmailConfirmation_Otp_Succeeds_AndDoesNotPersistRawValue()
    {
        var userId = await RegisterAsync("otpuser", "otp@example.com", "Passw0rd!");

        using var scope = _sp.CreateScope();
        var request = scope.ServiceProvider.GetRequiredService<RequestEmailConfirmationUseCase>();
        var confirm = scope.ServiceProvider.GetRequiredService<ConfirmEmailUseCase>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var issued = await request.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.Otp
        });

        Assert.True(issued.IsSuccess, issued.Error?.Description);
        Assert.NotNull(issued.Value.ChallengeId);
        Assert.Single(_dispatcher.Deliveries);
        var raw = _dispatcher.Deliveries[0].RawVerificationValue;
        Assert.False(string.IsNullOrWhiteSpace(raw));
        Assert.Equal(issued.Value.ChallengeId, _dispatcher.Deliveries[0].ChallengeId);

        var row = await db.VerificationChallenges.AsNoTracking()
            .SingleAsync(c => c.Id == issued.Value.ChallengeId);
        Assert.DoesNotContain(raw, System.Text.Json.JsonSerializer.Serialize(row));
        Assert.Null(typeof(VerificationChallenge).GetProperty("SecretHash"));

        var confirmed = await confirm.ExecuteAsync(new ConfirmEmailRequest
        {
            ChallengeId = issued.Value.ChallengeId!.Value,
            VerificationValue = raw
        });

        Assert.True(confirmed.IsSuccess, confirmed.Error?.Description);
        var user = await users.FindByIdAsync(userId.ToString());
        Assert.True(user!.EmailConfirmed);

        var consumed = await db.VerificationChallenges.AsNoTracking()
            .SingleAsync(c => c.Id == issued.Value.ChallengeId);
        Assert.NotNull(consumed.ConsumedAtUtc);
    }

    [Fact]
    public async Task EmailConfirmation_UrlToken_Succeeds()
    {
        var userId = await RegisterAsync("urluser", "url@example.com", "Passw0rd!");
        _dispatcher.Deliveries.Clear();

        using var scope = _sp.CreateScope();
        var request = scope.ServiceProvider.GetRequiredService<RequestEmailConfirmationUseCase>();
        var confirm = scope.ServiceProvider.GetRequiredService<ConfirmEmailUseCase>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var issued = await request.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.UrlToken
        });
        Assert.True(issued.IsSuccess, issued.Error?.Description);

        var confirmed = await confirm.ExecuteAsync(new ConfirmEmailRequest
        {
            ChallengeId = issued.Value.ChallengeId!.Value,
            VerificationValue = _dispatcher.Deliveries[^1].RawVerificationValue
        });

        Assert.True(confirmed.IsSuccess, confirmed.Error?.Description);
        Assert.True((await users.FindByIdAsync(userId.ToString()))!.EmailConfirmed);
    }

    [Fact]
    public async Task EmailConfirmation_Lifetimes_MatchOptions()
    {
        var userId = await RegisterAsync("lifeuser", "life@example.com", "Passw0rd!");
        _dispatcher.Deliveries.Clear();

        using var scope = _sp.CreateScope();
        var request = scope.ServiceProvider.GetRequiredService<RequestEmailConfirmationUseCase>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var otp = await request.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.Otp
        });
        var otpRow = await db.VerificationChallenges.AsNoTracking()
            .SingleAsync(c => c.Id == otp.Value.ChallengeId);
        Assert.Equal(TimeSpan.FromMinutes(5), otpRow.ExpiresAtUtc - otpRow.CreatedAtUtc);

        // Invalidate via cooldown wait by manually invalidating then requesting URL token
        var tracked = await db.VerificationChallenges.SingleAsync(c => c.Id == otp.Value.ChallengeId);
        tracked.Invalidate(clock.UtcNow);
        await db.SaveChangesAsync();

        var url = await request.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.UrlToken
        });
        var urlRow = await db.VerificationChallenges.AsNoTracking()
            .SingleAsync(c => c.Id == url.Value.ChallengeId);
        Assert.Equal(TimeSpan.FromHours(1), urlRow.ExpiresAtUtc - urlRow.CreatedAtUtc);
    }

    [Fact]
    public async Task Cooldown_ReturnsResendTooSoon_WithoutInvalidating()
    {
        var userId = await RegisterAsync("cooluser", "cool@example.com", "Passw0rd!");
        _dispatcher.Deliveries.Clear();

        using var scope = _sp.CreateScope();
        var request = scope.ServiceProvider.GetRequiredService<RequestEmailConfirmationUseCase>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var first = await request.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.Otp
        });
        Assert.True(first.IsSuccess);

        var second = await request.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.Otp
        });

        Assert.Equal(VerificationErrors.ResendTooSoon.Code, second.Error!.Code);
        var open = await db.VerificationChallenges.AsNoTracking()
            .Where(c => c.UserId == userId && c.InvalidatedAtUtc == null && c.ConsumedAtUtc == null)
            .ToListAsync();
        Assert.Single(open);
        Assert.Equal(first.Value.ChallengeId, open[0].Id);
    }

    [Fact]
    public async Task Reissue_AfterCooldown_InvalidatesPrevious()
    {
        var userId = await RegisterAsync("reissue", "reissue@example.com", "Passw0rd!");

        using (var scope = _sp.CreateScope())
        {
            var request = scope.ServiceProvider.GetRequiredService<RequestEmailConfirmationUseCase>();
            var first = await request.ExecuteAsync(new RequestEmailConfirmationRequest
            {
                UserId = userId,
                Method = VerificationMethod.Otp
            });
            Assert.True(first.IsSuccess);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var row = await db.VerificationChallenges.SingleAsync(c => c.Id == first.Value.ChallengeId);
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"UPDATE VerificationChallenges SET CreatedAtUtc = {DateTime.UtcNow.AddMinutes(-5)} WHERE Id = {first.Value.ChallengeId}");
            db.Entry(row).State = EntityState.Detached;

            var second = await request.ExecuteAsync(new RequestEmailConfirmationRequest
            {
                UserId = userId,
                Method = VerificationMethod.Otp
            });
            Assert.True(second.IsSuccess, second.Error?.Description);

            var previous = await db.VerificationChallenges.AsNoTracking()
                .SingleAsync(c => c.Id == first.Value.ChallengeId);
            Assert.NotNull(previous.InvalidatedAtUtc);
            Assert.NotEqual(first.Value.ChallengeId, second.Value.ChallengeId);
        }
    }

    [Fact]
    public async Task ConcurrentCreate_OnlyOneOpenChallenge()
    {
        var userId = await RegisterAsync("race", "race@example.com", "Passw0rd!");

        // Seed an expired open challenge so both requests pass cooldown and must invalidate+insert
        using (var seed = _sp.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            db.VerificationChallenges.Add(VerificationChallenge.Reconstitute(
                Guid.NewGuid(),
                userId,
                VerificationPurpose.EmailConfirmation,
                VerificationMethod.Otp,
                VerificationChannel.Email,
                "race@example.com",
                DateTime.UtcNow.AddMinutes(-10),
                DateTime.UtcNow.AddMinutes(-1),
                null,
                0,
                null));
            await db.SaveChangesAsync();
        }

        using var scope1 = _sp.CreateScope();
        using var scope2 = _sp.CreateScope();
        var r1 = scope1.ServiceProvider.GetRequiredService<RequestEmailConfirmationUseCase>();
        var r2 = scope2.ServiceProvider.GetRequiredService<RequestEmailConfirmationUseCase>();

        var t1 = r1.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.Otp
        });
        var t2 = r2.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.Otp
        });

        await Task.WhenAll(t1, t2);
        var results = new[] { t1.Result, t2.Result };
        var summary = string.Join(" | ", results.Select(r =>
            r.IsSuccess ? $"OK:{r.Value.ChallengeId}" : $"{r.Error?.Code}:{r.Error?.Description}"));

        Assert.True(results.Count(r => r.IsSuccess) <= 1, $"At most one success. Results: {summary}");
        Assert.Contains(results, r => r.IsSuccess);

        using var verify = _sp.CreateScope();
        var dbv = verify.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var open = await dbv.VerificationChallenges.AsNoTracking()
            .Where(c =>
                c.UserId == userId
                && c.Purpose == VerificationPurpose.EmailConfirmation
                && c.ConsumedAtUtc == null
                && c.InvalidatedAtUtc == null)
            .ToListAsync();
        Assert.True(open.Count == 1, $"Expected one open challenge, found {open.Count}. Results: {summary}");
    }

    [Fact]
    public async Task SecondOpenChallenge_IsRejectedByFilteredUniqueIndex()
    {
        var userId = await RegisterAsync("uniq", "uniq@example.com", "Passw0rd!");

        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;

        db.VerificationChallenges.Add(VerificationChallenge.Create(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "uniq@example.com",
            now.AddMinutes(5),
            createdAtUtc: now));
        await db.SaveChangesAsync();

        db.VerificationChallenges.Add(VerificationChallenge.Create(
            userId,
            VerificationPurpose.EmailConfirmation,
            VerificationMethod.Otp,
            VerificationChannel.Email,
            "uniq@example.com",
            now.AddMinutes(5),
            createdAtUtc: now));

        var ex = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.True(new SqlServerPersistenceExceptionClassifier().IsUniqueConstraintViolation(ex));
    }

    [Fact]
    public async Task FilteredUniqueIndex_IsPresent()
    {
        await using var connection = new SqlConnection(_connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT i.name, i.has_filter, i.filter_definition, i.is_unique
            FROM sys.indexes i
            INNER JOIN sys.tables t ON i.object_id = t.object_id
            WHERE t.name = 'VerificationChallenges'
              AND i.name = 'IX_VerificationChallenges_Open_UserId_Purpose_Destination'
            """;

        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        Assert.True(reader.GetBoolean(reader.GetOrdinal("is_unique")));
        Assert.True(reader.GetBoolean(reader.GetOrdinal("has_filter")));
        var filter = reader.GetString(reader.GetOrdinal("filter_definition"));
        Assert.Contains("ConsumedAtUtc", filter, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("InvalidatedAtUtc", filter, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PasswordReset_AntiEnumeration_UnknownEmail()
    {
        _dispatcher.Deliveries.Clear();
        using var scope = _sp.CreateScope();
        var request = scope.ServiceProvider.GetRequiredService<RequestPasswordResetUseCase>();
        var result = await request.ExecuteAsync(new RequestPasswordResetRequest
        {
            Email = "nobody@example.com"
        });

        Assert.True(result.IsSuccess);
        Assert.Empty(_dispatcher.Deliveries);
    }

    [Fact]
    public async Task PasswordReset_UrlToken_Succeeds_AndEnforcesPasswordPolicy()
    {
        var userId = await RegisterAsync("resetuser", "reset@example.com", "Passw0rd!");
        _dispatcher.Deliveries.Clear();

        using var scope = _sp.CreateScope();
        var request = scope.ServiceProvider.GetRequiredService<RequestPasswordResetUseCase>();
        var reset = scope.ServiceProvider.GetRequiredService<ResetPasswordWithVerificationUseCase>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var asked = await request.ExecuteAsync(new RequestPasswordResetRequest
        {
            Email = "reset@example.com"
        });
        Assert.True(asked.IsSuccess);
        Assert.Single(_dispatcher.Deliveries);

        var delivery = _dispatcher.Deliveries[0];
        var weak = await reset.ExecuteAsync(new ResetPasswordWithVerificationRequest
        {
            ChallengeId = delivery.ChallengeId,
            Token = delivery.RawVerificationValue,
            NewPassword = "short"
        });
        Assert.Equal(AuthenticationErrors.InvalidPassword.Code, weak.Error!.Code);

        var ok = await reset.ExecuteAsync(new ResetPasswordWithVerificationRequest
        {
            ChallengeId = delivery.ChallengeId,
            Token = delivery.RawVerificationValue,
            NewPassword = "N3wPassw0rd!"
        });
        Assert.True(ok.IsSuccess, ok.Error?.Description);

        var user = await users.FindByIdAsync(userId.ToString());
        Assert.True(await users.CheckPasswordAsync(user!, "N3wPassw0rd!"));

        var challenge = await db.VerificationChallenges.AsNoTracking()
            .SingleAsync(c => c.Id == delivery.ChallengeId);
        Assert.NotNull(challenge.ConsumedAtUtc);
    }

    [Fact]
    public async Task EmailChanged_AfterChallenge_RejectsConfirmation()
    {
        var userId = await RegisterAsync("chg", "before@example.com", "Passw0rd!");
        _dispatcher.Deliveries.Clear();

        using var scope = _sp.CreateScope();
        var request = scope.ServiceProvider.GetRequiredService<RequestEmailConfirmationUseCase>();
        var confirm = scope.ServiceProvider.GetRequiredService<ConfirmEmailUseCase>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

        var issued = await request.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.Otp
        });
        Assert.True(issued.IsSuccess);

        var user = await users.FindByIdAsync(userId.ToString());
        await users.SetEmailAsync(user!, "after@example.com");

        var result = await confirm.ExecuteAsync(new ConfirmEmailRequest
        {
            ChallengeId = issued.Value.ChallengeId!.Value,
            VerificationValue = _dispatcher.Deliveries[^1].RawVerificationValue
        });

        Assert.Equal(VerificationErrors.DestinationMismatch.Code, result.Error!.Code);
    }

    [Fact]
    public async Task Otp_Exhaustion_RequiresNewChallenge()
    {
        var userId = await RegisterAsync("exhaust", "exhaust@example.com", "Passw0rd!");
        _dispatcher.Deliveries.Clear();

        using var scope = _sp.CreateScope();
        var request = scope.ServiceProvider.GetRequiredService<RequestEmailConfirmationUseCase>();
        var confirm = scope.ServiceProvider.GetRequiredService<ConfirmEmailUseCase>();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var issued = await request.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.Otp
        });

        for (var i = 0; i < 4; i++)
        {
            var fail = await confirm.ExecuteAsync(new ConfirmEmailRequest
            {
                ChallengeId = issued.Value.ChallengeId!.Value,
                VerificationValue = "000000"
            });
            Assert.Equal(VerificationErrors.InvalidCode.Code, fail.Error!.Code);
        }

        var exhausted = await confirm.ExecuteAsync(new ConfirmEmailRequest
        {
            ChallengeId = issued.Value.ChallengeId!.Value,
            VerificationValue = "000000"
        });
        Assert.Equal(VerificationErrors.TooManyAttempts.Code, exhausted.Error!.Code);

        var row = await db.VerificationChallenges.AsNoTracking()
            .SingleAsync(c => c.Id == issued.Value.ChallengeId);
        Assert.NotNull(row.InvalidatedAtUtc);
        Assert.Equal(5, row.FailedAttempts);
    }

    [Fact]
    public async Task ConsumedChallenge_CannotBeReused()
    {
        var userId = await RegisterAsync("reuse", "reuse@example.com", "Passw0rd!");
        _dispatcher.Deliveries.Clear();

        using var scope = _sp.CreateScope();
        var request = scope.ServiceProvider.GetRequiredService<RequestEmailConfirmationUseCase>();
        var confirm = scope.ServiceProvider.GetRequiredService<ConfirmEmailUseCase>();

        var issued = await request.ExecuteAsync(new RequestEmailConfirmationRequest
        {
            UserId = userId,
            Method = VerificationMethod.UrlToken
        });
        var raw = _dispatcher.Deliveries[^1].RawVerificationValue;

        Assert.True((await confirm.ExecuteAsync(new ConfirmEmailRequest
        {
            ChallengeId = issued.Value.ChallengeId!.Value,
            VerificationValue = raw
        })).IsSuccess);

        var again = await confirm.ExecuteAsync(new ConfirmEmailRequest
        {
            ChallengeId = issued.Value.ChallengeId!.Value,
            VerificationValue = raw
        });
        Assert.Equal(VerificationErrors.AlreadyConsumed.Code, again.Error!.Code);
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
        });
        services.AddPermixaVerification();
        services.AddSingleton<IVerificationDispatcher>(_dispatcher);
        return services.BuildServiceProvider();
    }

    private async Task<Guid> RegisterAsync(string userName, string email, string password)
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
        return result.Value.UserId;
    }
}
