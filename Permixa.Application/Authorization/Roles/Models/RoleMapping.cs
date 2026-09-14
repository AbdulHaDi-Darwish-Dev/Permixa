using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Roles.Models;

internal static class RoleMapping
{
    public static RoleDto ToDto(IdentityRoleRecord role) =>
        new(role.Id, role.Name, role.RoleLevel);
}
