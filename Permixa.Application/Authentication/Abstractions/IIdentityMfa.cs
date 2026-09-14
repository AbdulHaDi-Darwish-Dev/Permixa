namespace Permixa.Application.Authentication.Abstractions;

public interface IIdentityMfa
{
    Task<MfaStatusSnapshot?> GetStatusAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<AuthenticatorSetupMaterial?> BeginSetupAsync(
        Guid userId,
        string issuer,
        CancellationToken cancellationToken = default);

    Task<bool> VerifyAuthenticatorCodeAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken = default);

    Task<IdentityMfaEnableResult> EnableAsync(
        Guid userId,
        int recoveryCodeCount,
        CancellationToken cancellationToken = default);

    Task<bool> DisableAndResetAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<IdentityRecoveryCodesResult> RegenerateRecoveryCodesAsync(
        Guid userId,
        int recoveryCodeCount,
        CancellationToken cancellationToken = default);

    Task<bool> RedeemRecoveryCodeAsync(
        Guid userId,
        string recoveryCode,
        CancellationToken cancellationToken = default);
}

public sealed record MfaStatusSnapshot(
    bool IsEnabled,
    bool HasAuthenticatorKey,
    int RecoveryCodesRemaining);

public sealed record AuthenticatorSetupMaterial(
    string SharedKey,
    string AuthenticatorUri);

public sealed class IdentityMfaEnableResult
{
    private IdentityMfaEnableResult(bool succeeded, bool alreadyEnabled, IReadOnlyList<string> recoveryCodes)
    {
        Succeeded = succeeded;
        AlreadyEnabled = alreadyEnabled;
        RecoveryCodes = recoveryCodes;
    }

    public bool Succeeded { get; }

    public bool AlreadyEnabled { get; }

    public IReadOnlyList<string> RecoveryCodes { get; }

    public static IdentityMfaEnableResult Success(IReadOnlyList<string> recoveryCodes) =>
        new(true, false, recoveryCodes);

    public static IdentityMfaEnableResult AlreadyOn() =>
        new(false, true, Array.Empty<string>());

    public static IdentityMfaEnableResult Failed() =>
        new(false, false, Array.Empty<string>());
}

public sealed class IdentityRecoveryCodesResult
{
    private IdentityRecoveryCodesResult(bool succeeded, bool notEnabled, IReadOnlyList<string> recoveryCodes)
    {
        Succeeded = succeeded;
        NotEnabled = notEnabled;
        RecoveryCodes = recoveryCodes;
    }

    public bool Succeeded { get; }

    public bool NotEnabled { get; }

    public IReadOnlyList<string> RecoveryCodes { get; }

    public static IdentityRecoveryCodesResult Success(IReadOnlyList<string> recoveryCodes) =>
        new(true, false, recoveryCodes);

    public static IdentityRecoveryCodesResult MfaDisabled() =>
        new(false, true, Array.Empty<string>());

    public static IdentityRecoveryCodesResult Failed() =>
        new(false, false, Array.Empty<string>());
}
