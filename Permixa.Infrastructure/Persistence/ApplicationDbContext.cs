using Permixa.Domain.Audit;
using Permixa.Domain.Authentication;
using Permixa.Domain.Authorization;
using Permixa.Domain.Verification;
using Permixa.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence;

public sealed class ApplicationDbContext
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<Permission> Permissions => Set<Permission>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserPermissionOverride> UserPermissionOverrides => Set<UserPermissionOverride>();

    public DbSet<AuthorizationState> AuthorizationStates => Set<AuthorizationState>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    public DbSet<MfaLoginChallenge> MfaLoginChallenges => Set<MfaLoginChallenge>();

    public DbSet<IamAuditLog> IamAuditLogs => Set<IamAuditLog>();

    public DbSet<VerificationChallenge> VerificationChallenges => Set<VerificationChallenge>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);

        var auditLog = builder.Model.FindEntityType(typeof(IamAuditLog));
        if (auditLog is not null)
        {
            foreach (var foreignKey in auditLog.GetForeignKeys().ToList())
                auditLog.RemoveForeignKey(foreignKey);
        }
    }
}
