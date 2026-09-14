namespace Permixa.Application.Verification.PasswordReset;

public sealed class RequestPasswordResetRequest
{
    public required string Email { get; init; }
}

public sealed class ResetPasswordWithVerificationRequest
{
    public required Guid ChallengeId { get; init; }

    public required string Token { get; init; }

    public required string NewPassword { get; init; }
}
