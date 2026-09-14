namespace Permixa.Application.Authentication.ChangePassword;

public sealed class ChangePasswordResult
{
    public required bool ReauthenticationRequired { get; init; }
}
