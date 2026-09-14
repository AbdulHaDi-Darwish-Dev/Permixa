namespace Permixa.Application.Verification;

/// <summary>
/// Application-neutral verification challenge policy. Defaults match Phase 6 approvals.
/// </summary>
public sealed class PermixaVerificationOptions
{
    public TimeSpan OtpLifetime { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan UrlTokenLifetime { get; set; } = TimeSpan.FromHours(1);

    public int MaxOtpAttempts { get; set; } = 5;

    public TimeSpan ResendCooldown { get; set; } = TimeSpan.FromSeconds(60);
}
