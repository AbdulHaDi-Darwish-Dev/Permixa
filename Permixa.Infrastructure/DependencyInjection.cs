using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Audit.Get;
using Permixa.Application.Authentication;
using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authentication.ChangePassword;
using Permixa.Application.Authentication.Login;
using Permixa.Application.Authentication.Logout;
using Permixa.Application.Authentication.Mfa;
using Permixa.Application.Authentication.Mfa.BeginSetup;
using Permixa.Application.Authentication.Mfa.Complete;
using Permixa.Application.Authentication.Mfa.Disable;
using Permixa.Application.Authentication.Mfa.Enable;
using Permixa.Application.Authentication.Mfa.GetMfaStatus;
using Permixa.Application.Authentication.Mfa.Regenerate;
using Permixa.Application.Authentication.Refresh;
using Permixa.Application.Authentication.Register;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.EffectivePermissions;
using Permixa.Application.Authorization.Hierarchy;
using Permixa.Application.Authorization.Permissions.Create;
using Permixa.Application.Authorization.Permissions.Get;
using Permixa.Application.Authorization.Permissions.Update;
using Permixa.Application.Authorization.RolePermissions.Assign;
using Permixa.Application.Authorization.RolePermissions.Get;
using Permixa.Application.Authorization.RolePermissions.Remove;
using Permixa.Application.Authorization.Roles.ChangePosition;
using Permixa.Application.Authorization.Roles.Create;
using Permixa.Application.Authorization.Roles.Delete;
using Permixa.Application.Authorization.Roles.Get;
using Permixa.Application.Authorization.Roles.Rename;
using Permixa.Application.Authorization.UserPermissionOverrides.Get;
using Permixa.Application.Authorization.UserPermissionOverrides.Remove;
using Permixa.Application.Authorization.UserPermissionOverrides.Set;
using Permixa.Application.Authorization.UserRoles.Assign;
using Permixa.Application.Authorization.UserRoles.Get;
using Permixa.Application.Authorization.UserRoles.Remove;
using Permixa.Application.Authorization.Sessions;
using Permixa.Application.Authorization.Sessions.Get;
using Permixa.Application.Authorization.Sessions.Revoke;
using Permixa.Application.Authorization.Users.ChangeEmail;
using Permixa.Application.Authorization.Users.Create;
using Permixa.Application.Authorization.Users.Disable;
using Permixa.Application.Authorization.Users.ForcePasswordReset;
using Permixa.Application.Authorization.Users.Get;
using Permixa.Application.Authorization.Users.Lock;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Identity.Abstractions;
using Permixa.Application.Verification;
using Permixa.Application.Verification.Abstractions;
using Permixa.Application.Verification.EmailChange;
using Permixa.Application.Verification.EmailConfirmation;
using Permixa.Application.Verification.PasswordReset;
using Permixa.Infrastructure.Audit;
using Permixa.Infrastructure.Authentication;
using Permixa.Infrastructure.Bootstrap;
using Permixa.Infrastructure.Caching;
using Permixa.Infrastructure.Email;
using Permixa.Infrastructure.Identity;
using Permixa.Infrastructure.Persistence;
using Permixa.Infrastructure.Persistence.Repositories;
using Permixa.Infrastructure.Time;
using Permixa.Infrastructure.Verification;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Net.Mail;
using System.Security.Cryptography;

namespace Permixa.Infrastructure;

public sealed class PermixaInfrastructureOptions
{
    /// <summary>
    /// SQL Server connection string. Must be supplied by the consuming application.
    /// There is no default — LocalDB/SQLite are not assumed.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Secure first-time IAM bootstrap. Disabled by default; host must enable and invoke explicitly.
    /// </summary>
    public PermixaBootstrapOptions Bootstrap { get; set; } = new();
}

/// <summary>
/// Options for <see cref="DependencyInjection.AddPermixaAuthentication"/>.
/// JWT secrets are required here, not by base Infrastructure registration.
/// </summary>
public sealed class PermixaAuthenticationRegistrationOptions
{
    /// <summary>
    /// Refresh-token lifetime and related authentication options.
    /// </summary>
    public PermixaAuthenticationOptions Authentication { get; set; } = new();

    /// <summary>
    /// JWT access-token signing and lifetime. Issuer, Audience, and PrivateKeyPem are required.
    /// </summary>
    public PermixaJwtOptions Jwt { get; set; } = new();

    /// <summary>
    /// Authenticator TOTP / recovery-code MFA v1 policy.
    /// </summary>
    public PermixaMfaOptions Mfa { get; set; } = new();
}

/// <summary>
/// Options for <see cref="DependencyInjection.AddPermixaVerification"/>.
/// </summary>
public sealed class PermixaVerificationRegistrationOptions
{
    public PermixaVerificationOptions Verification { get; set; } = new();
}

public static class DependencyInjection
{
    /// <summary>
    /// Registers Permixa Infrastructure: SQL Server DbContext, ASP.NET Core Identity,
    /// repositories, authorization services, memory permission cache, and bootstrap.
    /// Requires an explicit SQL Server connection string. Call this first among Permixa registrations.
    /// </summary>
    public static IServiceCollection AddPermixaInfrastructure(
        this IServiceCollection services,
        Action<PermixaInfrastructureOptions>? configure = null)
    {
        var options = new PermixaInfrastructureOptions();
        configure?.Invoke(options);

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            throw new InvalidOperationException(
                "Permixa Infrastructure requires an explicit SQL Server connection string. " +
                "Configure PermixaInfrastructureOptions.ConnectionString when calling AddPermixaInfrastructure. " +
                "There is no default provider or LocalDB assumption.");
        }

        var connectionString = options.ConnectionString;

        services.AddSingleton(Options.Create(CloneBootstrapOptions(options.Bootstrap)));
        services.AddDataProtection();

        services.AddDbContext<ApplicationDbContext>(db =>
        {
            db.UseSqlServer(connectionString);
            db.UseQueryTrackingBehavior(QueryTrackingBehavior.TrackAll);
        });

        services
            .AddIdentityCore<ApplicationUser>(identity =>
            {
                identity.User.RequireUniqueEmail = true;
                identity.Password.RequireDigit = true;
                identity.Password.RequireLowercase = true;
                identity.Password.RequireUppercase = true;
                identity.Password.RequireNonAlphanumeric = true;
                identity.Password.RequiredLength = 8;
                identity.Lockout.AllowedForNewUsers = true;
                identity.Lockout.MaxFailedAccessAttempts = 5;
                identity.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddRoles<ApplicationRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>()
            .AddDefaultTokenProviders();

        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IRolePermissionRepository, RolePermissionRepository>();
        services.AddScoped<IUserPermissionOverrideRepository, UserPermissionOverrideRepository>();
        services.AddScoped<IAuthorizationStateRepository, AuthorizationStateRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IMfaLoginChallengeRepository, MfaLoginChallengeRepository>();
        services.AddScoped<ISessionReader, SessionReader>();
        services.AddScoped<IVerificationChallengeRepository, VerificationChallengeRepository>();

        services.AddScoped<IIdentityUserReader, IdentityUserReader>();
        services.AddScoped<IIdentityUserWriter, IdentityUserWriter>();
        services.AddScoped<IIdentityPasswordChange, IdentityPasswordChange>();
        services.AddScoped<IIdentityMfa, IdentityMfa>();
        services.AddScoped<IIdentityUserCreator, IdentityUserCreator>();
        services.AddScoped<IIdentityRoleReader, IdentityRoleReader>();
        services.AddScoped<IIdentityRoleWriter, IdentityRoleWriter>();
        services.AddScoped<IIdentityUserRoleReader, IdentityUserRoleReader>();
        services.AddScoped<IIdentityUserRoleWriter, IdentityUserRoleWriter>();
        services.AddScoped<IUserAuthorizationVersionStore, UserAuthorizationVersionStore>();
        services.AddScoped<IRoleHierarchyReader, RoleHierarchyReader>();
        services.AddScoped<IAuthorizationHierarchyWriteLock, AuthorizationHierarchyWriteLock>();

        services.AddScoped<IAuthorizationHierarchyService, AuthorizationHierarchyService>();
        services.AddScoped<IEffectivePermissionService, EffectivePermissionService>();

        services.AddScoped<IUnitOfWork, UnitOfWork>();
        services.TryAddScoped<IIamAuditSink, SqlIamAuditSink>();
        services.TryAddScoped<IIamAuditReader, SqlIamAuditReader>();
        services.AddSingleton<IPersistenceExceptionClassifier, SqlServerPersistenceExceptionClassifier>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IPermissionCache, MemoryPermissionCache>();

        services.AddScoped<IAuthorizationStateBootstrapper, AuthorizationStateBootstrapper>();
        services.AddScoped<IamPermissionSeeder>();
        services.AddScoped<IPermixaBootstrapper, PermixaBootstrapper>();

        return services;
    }

    /// <summary>
    /// Registers consumer-facing authorization administration use cases.
    /// Requires <see cref="AddPermixaInfrastructure"/> first.
    /// Does not re-register <see cref="IEffectivePermissionService"/> or <see cref="IAuthorizationHierarchyService"/>.
    /// </summary>
    public static IServiceCollection AddPermixaAuthorization(this IServiceCollection services)
    {
        services.AddScoped<CreatePermissionUseCase>();
        services.AddScoped<GetPermissionByIdUseCase>();
        services.AddScoped<GetPermissionByNameUseCase>();
        services.AddScoped<GetPermissionsUseCase>();
        services.AddScoped<UpdatePermissionDescriptionUseCase>();

        services.AddScoped<AssignPermissionToRoleUseCase>();
        services.AddScoped<RemovePermissionFromRoleUseCase>();
        services.AddScoped<GetRolePermissionsUseCase>();

        services.AddScoped<SetUserPermissionOverrideUseCase>();
        services.AddScoped<RemoveUserPermissionOverrideUseCase>();
        services.AddScoped<GetUserPermissionOverridesUseCase>();

        services.AddScoped<GetEffectivePermissionsUseCase>();
        services.AddScoped<GetMyEffectivePermissionsUseCase>();

        services.AddScoped<CreateRoleUseCase>();
        services.AddScoped<GetRoleByIdUseCase>();
        services.AddScoped<GetRoleByNameUseCase>();
        services.AddScoped<GetRolesUseCase>();
        services.AddScoped<RenameRoleUseCase>();
        services.AddScoped<ChangeRolePositionUseCase>();
        services.AddScoped<DeleteRoleUseCase>();

        services.AddScoped<AssignRoleToUserUseCase>();
        services.AddScoped<RemoveRoleFromUserUseCase>();
        services.AddScoped<GetMyRolesUseCase>();
        services.AddScoped<GetUserRolesUseCase>();
        services.AddScoped<GetUsersInRoleUseCase>();

        services.AddScoped<AdminCreateUserUseCase>();
        services.AddScoped<GetUserByIdUseCase>();
        services.AddScoped<GetUserByEmailUseCase>();
        services.AddScoped<GetUsersUseCase>();
        services.AddScoped<GetUserIamDetailsUseCase>();
        services.AddScoped<LockUserUseCase>();
        services.AddScoped<UnlockUserUseCase>();
        services.AddScoped<DisableUserUseCase>();
        services.AddScoped<EnableUserUseCase>();

        services.AddScoped<GetMySessionsUseCase>();
        services.AddScoped<GetUserSessionsUseCase>();
        services.AddScoped<RevokeMySessionUseCase>();
        services.AddScoped<RevokeUserSessionUseCase>();
        services.AddScoped<RevokeAllMySessionsUseCase>();
        services.AddScoped<RevokeAllUserSessionsUseCase>();

        services.AddScoped<AdminRequestEmailChangeUseCase>();
        services.AddScoped<ForcePasswordResetUseCase>();
        services.AddScoped<GetIamAuditLogsUseCase>();

        return services;
    }

    /// <summary>
    /// Registers Authentication use cases, Identity auth adapters, refresh-token crypto, and RS256 JWT generation.
    /// Requires <see cref="AddPermixaInfrastructure"/> to be called first.
    /// JWT Issuer, Audience, and PrivateKeyPem are mandatory here.
    /// </summary>
    public static IServiceCollection AddPermixaAuthentication(
        this IServiceCollection services,
        Action<PermixaAuthenticationRegistrationOptions>? configure = null)
    {
        var options = new PermixaAuthenticationRegistrationOptions();
        configure?.Invoke(options);

        ValidateJwtOptions(options.Jwt);
        ValidateMfaOptions(options.Mfa);

        services.AddSingleton(Options.Create(CloneAuthenticationOptions(options.Authentication)));
        services.AddSingleton(Options.Create(CloneJwtOptions(options.Jwt)));
        services.AddSingleton(Options.Create(CloneMfaOptions(options.Mfa)));

        services.AddScoped<IIdentityAuthenticator, IdentityAuthenticator>();

        services.AddSingleton<IRefreshTokenCrypto, RefreshTokenCrypto>();
        services.AddSingleton<IAccessTokenGenerator, RsaJwtAccessTokenGenerator>();

        services.AddScoped<IAuthenticationTokenService, AuthenticationTokenService>();
        services.AddScoped<RegisterUserUseCase>();
        services.AddScoped<LoginUseCase>();
        services.AddScoped<RefreshAccessTokenUseCase>();
        services.AddScoped<LogoutUseCase>();
        services.AddScoped<ChangePasswordUseCase>();

        services.AddScoped<MfaLoginChallengeIssuer>();
        services.AddScoped<MfaLoginCompletion>();
        services.AddScoped<GetMfaStatusUseCase>();
        services.AddScoped<BeginAuthenticatorSetupUseCase>();
        services.AddScoped<EnableAuthenticatorMfaUseCase>();
        services.AddScoped<DisableMfaUseCase>();
        services.AddScoped<RegenerateRecoveryCodesUseCase>();
        services.AddScoped<CompleteMfaWithTotpUseCase>();
        services.AddScoped<CompleteMfaWithRecoveryCodeUseCase>();

        return services;
    }

    /// <summary>
    /// Registers verification use cases, Identity token adapters, and challenge policy options.
    /// Requires <see cref="AddPermixaInfrastructure"/> first.
    /// Hosts must register <see cref="IVerificationDispatcher"/> (Phase 7 providers); not registered here.
    /// </summary>
    public static IServiceCollection AddPermixaVerification(
        this IServiceCollection services,
        Action<PermixaVerificationRegistrationOptions>? configure = null)
    {
        var options = new PermixaVerificationRegistrationOptions();
        configure?.Invoke(options);
        ValidateVerificationOptions(options.Verification);

        services.AddSingleton(Options.Create(CloneVerificationOptions(options.Verification)));

        services.AddScoped<IVerificationTokenProvider, IdentityVerificationTokenProvider>();
        services.AddScoped<IIdentityUserEmailReader, IdentityUserEmailReader>();
        services.AddScoped<IIdentityEmailConfirmation, IdentityEmailConfirmation>();
        services.AddScoped<IIdentityPasswordReset, IdentityPasswordReset>();
        services.AddScoped<IIdentityEmailChange, IdentityEmailChange>();
        services.AddSingleton<IPersistenceExceptionClassifier, SqlServerPersistenceExceptionClassifier>();

        services.AddScoped<VerificationChallengeIssuer>();
        services.AddScoped<PendingEmailChangeService>();
        services.AddScoped<RequestEmailConfirmationUseCase>();
        services.AddScoped<ConfirmEmailUseCase>();
        services.AddScoped<RequestPasswordResetUseCase>();
        services.AddScoped<ResetPasswordWithVerificationUseCase>();
        services.AddScoped<RequestEmailChangeUseCase>();
        services.AddScoped<ConfirmEmailChangeUseCase>();

        return services;
    }

    /// <summary>
    /// Registers the provider-neutral email delivery pipeline (templates, links, dispatcher, options).
    /// Does not register an <see cref="IEmailSender"/> transport — supply one via a provider package
    /// (e.g. Permixa.Email.Resend) or a host implementation.
    /// Optional — Verification Engine works without this when a host supplies its own <see cref="IVerificationDispatcher"/>.
    /// </summary>
    public static IServiceCollection AddPermixaEmailDelivery(
        this IServiceCollection services,
        Action<PermixaEmailDeliveryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new PermixaEmailDeliveryOptions();
        configure(options);
        ValidateEmailDeliveryOptions(options);

        services.AddSingleton(Options.Create(CloneEmailDeliveryOptions(options)));

        services.AddSingleton<IEmailTemplateRenderer, EmbeddedEmailTemplateRenderer>();
        services.AddSingleton<IVerificationLinkBuilder, ConfiguredVerificationLinkBuilder>();
        services.AddScoped<IVerificationDispatcher, EmailVerificationDispatcher>();

        return services;
    }

    private static void ValidateEmailDeliveryOptions(PermixaEmailDeliveryOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.FromEmail) || !IsBasicEmail(options.FromEmail))
        {
            throw new InvalidOperationException(
                "Permixa email delivery requires a valid FromEmail.");
        }

        if (string.IsNullOrWhiteSpace(options.FromName))
        {
            throw new InvalidOperationException(
                "Permixa email delivery requires FromName.");
        }

        if (string.IsNullOrWhiteSpace(options.Branding.ApplicationName))
        {
            throw new InvalidOperationException(
                "Permixa email delivery requires Branding.ApplicationName.");
        }

        if (!string.IsNullOrWhiteSpace(options.Branding.SupportEmail)
            && !IsBasicEmail(options.Branding.SupportEmail))
        {
            throw new InvalidOperationException(
                "Permixa email delivery Branding.SupportEmail is invalid.");
        }

        // Phase 7 ships OTP + URL flows; both templates are required for the default link builder.
        ValidateUrlTemplate(
            options.EmailConfirmationUrlTemplate,
            nameof(PermixaEmailDeliveryOptions.EmailConfirmationUrlTemplate));
        ValidateUrlTemplate(
            options.PasswordResetUrlTemplate,
            nameof(PermixaEmailDeliveryOptions.PasswordResetUrlTemplate));
    }

    private static void ValidateUrlTemplate(string template, string name)
    {
        if (string.IsNullOrWhiteSpace(template))
        {
            throw new InvalidOperationException(
                $"Permixa email delivery requires {name}.");
        }

        if (!template.Contains("{challengeId}", StringComparison.Ordinal)
            || !template.Contains("{token}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Permixa email delivery {name} must contain {{challengeId}} and {{token}} placeholders.");
        }

        if (!Uri.TryCreate(
                template
                    .Replace("{challengeId}", Guid.Empty.ToString("D"), StringComparison.Ordinal)
                    .Replace("{token}", "token", StringComparison.Ordinal),
                UriKind.Absolute,
                out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Permixa email delivery {name} must be an absolute http(s) URL template.");
        }
    }

    private static bool IsBasicEmail(string value)
    {
        try
        {
            _ = new MailAddress(value);
            return value.Contains('@', StringComparison.Ordinal);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static PermixaEmailDeliveryOptions CloneEmailDeliveryOptions(
        PermixaEmailDeliveryOptions source) =>
        new()
        {
            FromEmail = source.FromEmail,
            FromName = source.FromName,
            EmailConfirmationUrlTemplate = source.EmailConfirmationUrlTemplate,
            PasswordResetUrlTemplate = source.PasswordResetUrlTemplate,
            Branding = new PermixaEmailBrandingOptions
            {
                ApplicationName = source.Branding.ApplicationName,
                CompanyName = source.Branding.CompanyName,
                LogoUrl = source.Branding.LogoUrl,
                SupportEmail = source.Branding.SupportEmail
            }
        };

    private static void ValidateVerificationOptions(PermixaVerificationOptions options)
    {
        if (options.OtpLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Permixa verification OtpLifetime must be greater than zero.");
        }

        if (options.UrlTokenLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Permixa verification UrlTokenLifetime must be greater than zero.");
        }

        if (options.MaxOtpAttempts <= 0)
        {
            throw new InvalidOperationException(
                "Permixa verification MaxOtpAttempts must be greater than zero.");
        }

        if (options.ResendCooldown < TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Permixa verification ResendCooldown cannot be negative.");
        }
    }

    private static PermixaVerificationOptions CloneVerificationOptions(
        PermixaVerificationOptions source) =>
        new()
        {
            OtpLifetime = source.OtpLifetime,
            UrlTokenLifetime = source.UrlTokenLifetime,
            MaxOtpAttempts = source.MaxOtpAttempts,
            ResendCooldown = source.ResendCooldown
        };

    private static void ValidateJwtOptions(PermixaJwtOptions jwt)
    {
        if (string.IsNullOrWhiteSpace(jwt.Issuer))
        {
            throw new InvalidOperationException(
                "Permixa JWT Issuer is required. Configure PermixaAuthenticationRegistrationOptions.Jwt.Issuer.");
        }

        if (string.IsNullOrWhiteSpace(jwt.Audience))
        {
            throw new InvalidOperationException(
                "Permixa JWT Audience is required. Configure PermixaAuthenticationRegistrationOptions.Jwt.Audience.");
        }

        if (string.IsNullOrWhiteSpace(jwt.PrivateKeyPem))
        {
            throw new InvalidOperationException(
                "Permixa JWT PrivateKeyPem is required. Supply an RSA private key (PEM) via configuration. " +
                "Do not hardcode production signing keys.");
        }

        try
        {
            using var rsa = RSA.Create();
            rsa.ImportFromPem(jwt.PrivateKeyPem);

            if (rsa.KeySize < 2048)
            {
                throw new InvalidOperationException(
                    "Permixa JWT RSA key must be at least 2048 bits.");
            }
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                "Permixa JWT PrivateKeyPem is invalid or could not be imported as an RSA private key.",
                ex);
        }
    }

    private static PermixaBootstrapOptions CloneBootstrapOptions(PermixaBootstrapOptions source) =>
        new()
        {
            Enabled = source.Enabled,
            OwnerEmail = source.OwnerEmail,
            OwnerUserName = source.OwnerUserName,
            OwnerPassword = source.OwnerPassword
        };

    private static PermixaAuthenticationOptions CloneAuthenticationOptions(
        PermixaAuthenticationOptions source) =>
        new()
        {
            RefreshTokenLifetime = source.RefreshTokenLifetime,
            RequireConfirmedEmail = source.RequireConfirmedEmail
        };

    private static PermixaJwtOptions CloneJwtOptions(PermixaJwtOptions source) =>
        new()
        {
            Issuer = source.Issuer,
            Audience = source.Audience,
            PrivateKeyPem = source.PrivateKeyPem,
            AccessTokenLifetime = source.AccessTokenLifetime
        };

    private static void ValidateMfaOptions(PermixaMfaOptions options)
    {
        if (options.ChallengeLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException(
                "Permixa MFA ChallengeLifetime must be greater than zero.");
        }

        if (options.MaxAttempts <= 0)
        {
            throw new InvalidOperationException(
                "Permixa MFA MaxAttempts must be greater than zero.");
        }

        if (options.RecoveryCodeCount <= 0)
        {
            throw new InvalidOperationException(
                "Permixa MFA RecoveryCodeCount must be greater than zero.");
        }

        if (string.IsNullOrWhiteSpace(options.Issuer))
        {
            throw new InvalidOperationException(
                "Permixa MFA Issuer is required.");
        }
    }

    private static PermixaMfaOptions CloneMfaOptions(PermixaMfaOptions source) =>
        new()
        {
            ChallengeLifetime = source.ChallengeLifetime,
            MaxAttempts = source.MaxAttempts,
            RecoveryCodeCount = source.RecoveryCodeCount,
            Issuer = source.Issuer
        };
}
