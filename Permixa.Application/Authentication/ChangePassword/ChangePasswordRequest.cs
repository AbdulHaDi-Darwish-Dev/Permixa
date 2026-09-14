namespace Permixa.Application.Authentication.ChangePassword;

public sealed class ChangePasswordRequest
{
    public Guid UserId { get; init; }

    public string CurrentPassword { get; init; } = string.Empty;

    public string NewPassword { get; init; } = string.Empty;

    public Guid? CurrentFamilyId { get; init; }
}
