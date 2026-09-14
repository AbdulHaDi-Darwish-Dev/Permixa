using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.Permissions.Models;

internal static class PermissionMapping
{
    public static PermissionDto ToDto(Permission permission) =>
        new(
            permission.Id,
            permission.Name,
            permission.Description,
            permission.CreatedAtUtc,
            permission.UpdatedAtUtc);
}
