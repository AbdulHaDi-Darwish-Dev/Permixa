using Permixa.Application.Authentication.Models;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authentication.Refresh;

public sealed class RefreshAccessTokenUseCase
{
    private readonly IAuthenticationTokenService _tokens;

    public RefreshAccessTokenUseCase(IAuthenticationTokenService tokens)
    {
        _tokens = tokens;
    }

    public Task<Result<AuthenticationResult>> ExecuteAsync(
        RefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _tokens.RotateAsync(request.RefreshToken, cancellationToken);
    }
}
