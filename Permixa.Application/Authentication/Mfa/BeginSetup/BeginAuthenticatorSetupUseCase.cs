using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Common.Results;
using Microsoft.Extensions.Options;

namespace Permixa.Application.Authentication.Mfa.BeginSetup;

public sealed class BeginAuthenticatorSetupUseCase
{
    private readonly IIdentityMfa _mfa;
    private readonly PermixaMfaOptions _options;

    public BeginAuthenticatorSetupUseCase(
        IIdentityMfa mfa,
        IOptions<PermixaMfaOptions> options)
    {
        _mfa = mfa;
        _options = options.Value;
    }

    public async Task<Result<AuthenticatorSetupResult>> ExecuteAsync(
        BeginAuthenticatorSetupCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.UserId == Guid.Empty)
            return Result.Failure<AuthenticatorSetupResult>(AuthorizationErrors.UserNotFound);

        var setup = await _mfa.BeginSetupAsync(command.UserId, _options.Issuer, cancellationToken);
        if (setup is null)
            return Result.Failure<AuthenticatorSetupResult>(AuthorizationErrors.UserNotFound);

        return Result.Success(new AuthenticatorSetupResult(setup.SharedKey, setup.AuthenticatorUri));
    }
}
