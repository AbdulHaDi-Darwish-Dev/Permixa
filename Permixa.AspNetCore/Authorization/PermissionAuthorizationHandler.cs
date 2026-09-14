using Permixa.AspNetCore.Security;
using Permixa.Application.Authorization.Abstractions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Logging;

namespace Permixa.AspNetCore.Authorization;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ICurrentUser _currentUser;
    private readonly IEffectivePermissionService _effectivePermissions;
    private readonly ILogger<PermissionAuthorizationHandler> _logger;

    public PermissionAuthorizationHandler(
        ICurrentUser currentUser,
        IEffectivePermissionService effectivePermissions,
        ILogger<PermissionAuthorizationHandler> logger)
    {
        _currentUser = currentUser;
        _effectivePermissions = effectivePermissions;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (!_currentUser.IsAuthenticated || _currentUser.UserId is null)
        {
            // Unauthenticated / invalid Permixa identity → do not Succeed (401 via AuthN challenge).
            return;
        }

        var result = await _effectivePermissions.HasPermissionAsync(
            _currentUser.UserId.Value,
            requirement.PermissionName);

        if (result.IsFailure)
        {
            _logger.LogWarning(
                "Permission authorization failed closed for policy {Permission}. Code={Code}",
                requirement.PermissionName,
                result.Error!.Code);
            return;
        }

        if (result.Value)
        {
            context.Succeed(requirement);
            return;
        }

        _logger.LogInformation(
            "Permission denied for user. Permission={Permission}",
            requirement.PermissionName);
    }
}
