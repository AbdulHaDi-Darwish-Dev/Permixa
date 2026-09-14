using System.Text.RegularExpressions;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authorization.Permissions;

/// <summary>
/// Application-level permission name convention: Resource.Action (one or more dotted segments).
/// Domain only requires non-empty names; this enforces a stable catalog format.
/// </summary>
public static partial class PermissionName
{
    /// <summary>
    /// At least two segments separated by '.', each starting with a letter,
    /// then letters or digits only. Examples: Users.Read, Iam.RolePermissions.Manage.
    /// </summary>
    [GeneratedRegex(@"^[A-Za-z][A-Za-z0-9]*(\.[A-Za-z][A-Za-z0-9]*)+$", RegexOptions.CultureInvariant)]
    private static partial Regex FormatRegex();

    public static Result Validate(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return Result.Failure(AuthorizationErrors.InvalidPermission);

        var trimmed = name.Trim();
        if (!FormatRegex().IsMatch(trimmed))
            return Result.Failure(AuthorizationErrors.InvalidPermission);

        return Result.Success();
    }

    public static string Normalize(string name) => name.Trim();
}
