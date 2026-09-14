namespace Permixa.Application.Authentication.Models;

/// <summary>
/// Login-only discriminated success. Exactly one of <see cref="Authenticated"/> or <see cref="MfaRequired"/>.
/// </summary>
public sealed class LoginResult
{
    private LoginResult(AuthenticationResult? authentication, MfaRequiredResult? mfa)
    {
        Authentication = authentication;
        Mfa = mfa;
    }

    public AuthenticationResult? Authentication { get; }

    public MfaRequiredResult? Mfa { get; }

    public bool IsAuthenticated => Authentication is not null;

    public bool IsMfaRequired => Mfa is not null;

    public static LoginResult Authenticated(AuthenticationResult authentication)
    {
        ArgumentNullException.ThrowIfNull(authentication);
        return new LoginResult(authentication, null);
    }

    public static LoginResult MfaRequired(string mfaProof, DateTime expiresAtUtc)
    {
        if (string.IsNullOrWhiteSpace(mfaProof))
            throw new ArgumentException("MFA proof cannot be empty.", nameof(mfaProof));

        return new LoginResult(null, new MfaRequiredResult(mfaProof.Trim(), expiresAtUtc));
    }
}

public sealed record MfaRequiredResult(string MfaProof, DateTime ExpiresAtUtc);
