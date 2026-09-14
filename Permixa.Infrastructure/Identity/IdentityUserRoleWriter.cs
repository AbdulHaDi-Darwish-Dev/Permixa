using Permixa.Application.Identity.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityUserRoleWriter : IIdentityUserRoleWriter
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly RoleManager<ApplicationRole> _roles;

    public IdentityUserRoleWriter(
        UserManager<ApplicationUser> users,
        RoleManager<ApplicationRole> roles)
    {
        _users = users;
        _roles = roles;
    }

    public async Task<bool> AddToRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        var role = await _roles.FindByIdAsync(roleId.ToString());
        if (user is null || role is null || string.IsNullOrWhiteSpace(role.Name))
            return false;

        if (await _users.IsInRoleAsync(user, role.Name))
            return false;

        var result = await _users.AddToRoleAsync(user, role.Name);
        if (result.Succeeded)
            return true;

        if (await _users.IsInRoleAsync(user, role.Name))
            return false;

        throw new InvalidOperationException(
            "Failed to assign role: " + string.Join("; ", result.Errors.Select(e => e.Description)));
    }

    public async Task<bool> RemoveFromRoleAsync(
        Guid userId,
        Guid roleId,
        CancellationToken cancellationToken = default)
    {
        var user = await _users.FindByIdAsync(userId.ToString());
        var role = await _roles.FindByIdAsync(roleId.ToString());
        if (user is null || role is null || string.IsNullOrWhiteSpace(role.Name))
            return false;

        if (!await _users.IsInRoleAsync(user, role.Name))
            return false;

        var result = await _users.RemoveFromRoleAsync(user, role.Name);
        if (result.Succeeded)
            return true;

        if (!await _users.IsInRoleAsync(user, role.Name))
            return false;

        throw new InvalidOperationException(
            "Failed to remove role: " + string.Join("; ", result.Errors.Select(e => e.Description)));
    }
}
