namespace Permixa.Application.Authorization.Sessions.Models;

internal static class SessionMapping
{
    public static SessionDto ToDto(SessionFamilyRecord family, Guid? currentFamilyId) =>
        new(
            family.FamilyId,
            family.CreatedAtUtc,
            family.ExpiresAtUtc,
            currentFamilyId is Guid current && current == family.FamilyId);
}
