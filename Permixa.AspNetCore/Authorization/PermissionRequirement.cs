using AppPermissionName = Permixa.Application.Authorization.Permissions.PermissionName;
using Microsoft.AspNetCore.Authorization;

namespace Permixa.AspNetCore.Authorization;

public sealed class PermissionRequirement : IAuthorizationRequirement
{
    public PermissionRequirement(string permissionName)
    {
        var validation = AppPermissionName.Validate(permissionName);
        if (validation.IsFailure)
        {
            throw new InvalidOperationException(
                $"Invalid Permixa permission name '{permissionName}': {validation.Error!.Description}");
        }

        PermissionName = AppPermissionName.Normalize(permissionName);
    }

    public string PermissionName { get; }
}
