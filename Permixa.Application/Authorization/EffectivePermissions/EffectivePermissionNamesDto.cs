namespace Permixa.Application.Authorization.EffectivePermissions;

public sealed record EffectivePermissionNamesDto(IReadOnlyList<string> Permissions)
{
    public static EffectivePermissionNamesDto FromNames(IEnumerable<string> permissions) =>
        new(permissions.OrderBy(name => name, StringComparer.Ordinal).ToList());
}
