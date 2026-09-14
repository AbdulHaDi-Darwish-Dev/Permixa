using Permixa.Application.Authentication.Models;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authentication.Logout;

public sealed class LogoutUseCase
{
    private readonly IAuthenticationTokenService _tokens;

    public LogoutUseCase(IAuthenticationTokenService tokens)
    {
        _tokens = tokens;
    }

    public Task<Result> ExecuteAsync(
        RevokeRefreshTokenRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        return _tokens.RevokeAsync(request.RefreshToken, cancellationToken);
    }
}
