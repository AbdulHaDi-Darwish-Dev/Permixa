using Permixa.Application.Identity.Abstractions;
using Permixa.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityRoleWriter : IIdentityRoleWriter
{
    private readonly RoleManager<ApplicationRole> _roles;
    private readonly ApplicationDbContext _db;

    public IdentityRoleWriter(RoleManager<ApplicationRole> roles, ApplicationDbContext db)
    {
        _roles = roles;
        _db = db;
    }

    public async Task<IdentityRoleMutationResult> CreateAsync(
        string name,
        int roleLevel,
        CancellationToken cancellationToken = default)
    {
        var role = new ApplicationRole(name, roleLevel);
        var result = await _roles.CreateAsync(role);
        if (result.Succeeded)
            return IdentityRoleMutationResult.Success(role.Id);

        return Map(result);
    }

    public async Task<IdentityRoleMutationResult> RenameAsync(
        Guid roleId,
        string newName,
        CancellationToken cancellationToken = default)
    {
        var role = await _roles.FindByIdAsync(roleId.ToString());
        if (role is null)
            return IdentityRoleMutationResult.Failed(IdentityRoleMutationFailure.NotFound);

        var named = await _roles.SetRoleNameAsync(role, newName);
        if (!named.Succeeded)
            return Map(named);

        var updated = await _roles.UpdateAsync(role);
        return updated.Succeeded
            ? IdentityRoleMutationResult.Success(role.Id)
            : Map(updated);
    }

    public async Task ShiftTiersAsync(
        IReadOnlyCollection<int> fromLevels,
        Guid? excludeRoleId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fromLevels);

        var levels = fromLevels.Distinct().ToArray();
        if (levels.Length == 0)
            return;

        var query = _db.Roles.Where(r => levels.Contains(r.RoleLevel));
        if (excludeRoleId is Guid excluded)
            query = query.Where(r => r.Id != excluded);

        var roles = await query.ToListAsync(cancellationToken);

        foreach (var fromLevel in levels.OrderByDescending(level => level))
        {
            var next = checked(fromLevel + 1);
            foreach (var role in roles)
            {
                if (role.RoleLevel == fromLevel)
                    role.SetRoleLevel(next);
            }
        }
    }

    public async Task SetRoleLevelAsync(
        Guid roleId,
        int roleLevel,
        CancellationToken cancellationToken = default)
    {
        var role = await _db.Roles.FirstOrDefaultAsync(r => r.Id == roleId, cancellationToken)
            ?? throw new InvalidOperationException($"Role '{roleId}' was not found.");

        role.SetRoleLevel(roleLevel);
    }

    public async Task<IdentityRoleMutationResult> DeleteAsync(
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var role = await _roles.FindByIdAsync(roleId.ToString());
        if (role is null)
            return IdentityRoleMutationResult.Failed(IdentityRoleMutationFailure.NotFound);

        var result = await _roles.DeleteAsync(role);
        return result.Succeeded
            ? IdentityRoleMutationResult.Success(role.Id)
            : Map(result);
    }

    private static IdentityRoleMutationResult Map(IdentityResult result)
    {
        if (result.Errors.Any(e =>
                e.Code.Equals("DuplicateRoleName", StringComparison.OrdinalIgnoreCase)
                || e.Code.Equals("DuplicateName", StringComparison.OrdinalIgnoreCase)))
        {
            return IdentityRoleMutationResult.Failed(IdentityRoleMutationFailure.DuplicateName);
        }

        if (result.Errors.Any(e =>
                e.Code.Equals("InvalidRoleName", StringComparison.OrdinalIgnoreCase)
                || e.Code.Equals("InvalidName", StringComparison.OrdinalIgnoreCase)))
        {
            return IdentityRoleMutationResult.Failed(IdentityRoleMutationFailure.InvalidName);
        }

        return IdentityRoleMutationResult.Failed(IdentityRoleMutationFailure.Failed);
    }
}
