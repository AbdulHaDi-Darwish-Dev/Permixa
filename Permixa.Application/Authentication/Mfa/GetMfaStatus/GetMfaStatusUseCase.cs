using Permixa.Application.Authentication.Abstractions;
using Permixa.Application.Authorization;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authentication.Mfa.GetMfaStatus;

public sealed class GetMfaStatusUseCase
{
    private readonly IIdentityMfa _mfa;

    public GetMfaStatusUseCase(IIdentityMfa mfa)
    {
        _mfa = mfa;
    }

    public async Task<Result<MfaStatusResult>> ExecuteAsync(
        GetMfaStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.UserId == Guid.Empty)
            return Result.Failure<MfaStatusResult>(AuthorizationErrors.UserNotFound);

        var status = await _mfa.GetStatusAsync(query.UserId, cancellationToken);
        if (status is null)
            return Result.Failure<MfaStatusResult>(AuthorizationErrors.UserNotFound);

        return Result.Success(new MfaStatusResult(
            status.IsEnabled,
            status.HasAuthenticatorKey,
            status.RecoveryCodesRemaining));
    }
}
