using System.Text;
using Permixa.Application.Authentication.Abstractions;
using Microsoft.AspNetCore.Identity;

namespace Permixa.Infrastructure.Identity;

public sealed class IdentityMfa : IIdentityMfa
{
    private readonly UserManager<ApplicationUser> _users;

    public IdentityMfa(UserManager<ApplicationUser> users)
    {
        _users = users;
    }

    public async Task<MfaStatusSnapshot?> GetStatusAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
            return null;

        var key = await _users.GetAuthenticatorKeyAsync(user);
        var remaining = await _users.CountRecoveryCodesAsync(user);
        return new MfaStatusSnapshot(user.TwoFactorEnabled, !string.IsNullOrEmpty(key), remaining);
    }

    public async Task<AuthenticatorSetupMaterial?> BeginSetupAsync(
        Guid userId,
        string issuer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
            return null;

        var key = await _users.GetAuthenticatorKeyAsync(user);
        if (string.IsNullOrEmpty(key))
        {
            await _users.ResetAuthenticatorKeyAsync(user);
            key = await _users.GetAuthenticatorKeyAsync(user);
        }

        if (string.IsNullOrEmpty(key))
            throw new InvalidOperationException("Identity did not produce an authenticator key.");

        var account = user.Email ?? user.UserName ?? user.Id.ToString("D");
        return new AuthenticatorSetupMaterial(
            FormatSharedKey(key),
            BuildAuthenticatorUri(issuer, account, key));
    }

    public async Task<bool> VerifyAuthenticatorCodeAsync(
        Guid userId,
        string code,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        return await _users.VerifyTwoFactorTokenAsync(
            user,
            TokenOptions.DefaultAuthenticatorProvider,
            code);
    }

    public async Task<IdentityMfaEnableResult> EnableAsync(
        Guid userId,
        int recoveryCodeCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
            return IdentityMfaEnableResult.Failed();

        if (user.TwoFactorEnabled)
            return IdentityMfaEnableResult.AlreadyOn();

        var enabled = await _users.SetTwoFactorEnabledAsync(user, true);
        if (!enabled.Succeeded)
            return IdentityMfaEnableResult.Failed();

        var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, recoveryCodeCount);
        return IdentityMfaEnableResult.Success((codes ?? Array.Empty<string>()).ToArray());
    }

    public async Task<bool> DisableAndResetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        var disabled = await _users.SetTwoFactorEnabledAsync(user, false);
        if (!disabled.Succeeded)
            return false;

        var reset = await _users.ResetAuthenticatorKeyAsync(user);
        if (!reset.Succeeded)
            return false;

        await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, 0);
        return true;
    }

    public async Task<IdentityRecoveryCodesResult> RegenerateRecoveryCodesAsync(
        Guid userId,
        int recoveryCodeCount,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
            return IdentityRecoveryCodesResult.Failed();

        if (!user.TwoFactorEnabled)
            return IdentityRecoveryCodesResult.MfaDisabled();

        var codes = await _users.GenerateNewTwoFactorRecoveryCodesAsync(user, recoveryCodeCount);
        return IdentityRecoveryCodesResult.Success((codes ?? Array.Empty<string>()).ToArray());
    }

    public async Task<bool> RedeemRecoveryCodeAsync(
        Guid userId,
        string recoveryCode,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null)
            return false;

        var redeemed = await _users.RedeemTwoFactorRecoveryCodeAsync(user, recoveryCode);
        return redeemed.Succeeded;
    }

    internal static string FormatSharedKey(string unformattedKey)
    {
        var result = new StringBuilder();
        var current = 0;
        while (current + 4 < unformattedKey.Length)
        {
            result.Append(unformattedKey.AsSpan(current, 4)).Append(' ');
            current += 4;
        }

        if (current < unformattedKey.Length)
            result.Append(unformattedKey.AsSpan(current));

        return result.ToString().ToLowerInvariant();
    }

    internal static string BuildAuthenticatorUri(string issuer, string account, string unformattedKey)
    {
        var safeIssuer = string.IsNullOrWhiteSpace(issuer) ? "Permixa" : issuer.Trim();
        return string.Concat(
            "otpauth://totp/",
            Uri.EscapeDataString(safeIssuer),
            ":",
            Uri.EscapeDataString(account),
            "?secret=",
            unformattedKey,
            "&issuer=",
            Uri.EscapeDataString(safeIssuer),
            "&digits=6");
    }
}
