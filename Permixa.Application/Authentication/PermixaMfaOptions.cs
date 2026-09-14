namespace Permixa.Application.Authentication;

/// <summary>
/// Focused MFA v1 policy. Authenticator TOTP and recovery codes only.
/// </summary>
public sealed class PermixaMfaOptions
{
    public TimeSpan ChallengeLifetime { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; set; } = 5;

    public int RecoveryCodeCount { get; set; } = 10;

    /// <summary>
    /// otpauth issuer label. Default is <c>Permixa</c>.
    /// </summary>
    public string Issuer { get; set; } = "Permixa";
}
