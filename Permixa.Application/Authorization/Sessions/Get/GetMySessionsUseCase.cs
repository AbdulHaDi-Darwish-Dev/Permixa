using Permixa.Application.Authorization.Sessions.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Application.Identity.Abstractions;

namespace Permixa.Application.Authorization.Sessions.Get;

public sealed class GetMySessionsUseCase
{
    private readonly IIdentityUserReader _users;
    private readonly ISessionReader _sessions;
    private readonly IClock _clock;

    public GetMySessionsUseCase(
        IIdentityUserReader users,
        ISessionReader sessions,
        IClock clock)
    {
        _users = users;
        _sessions = sessions;
        _clock = clock;
    }

    public async Task<Result<IReadOnlyList<SessionDto>>> ExecuteAsync(
        GetMySessionsQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        if (query.UserId == Guid.Empty || !await _users.UserExistsAsync(query.UserId, cancellationToken))
            return Result.Failure<IReadOnlyList<SessionDto>>(AuthorizationErrors.UserNotFound);

        var families = await _sessions.GetActiveFamiliesForUserAsync(
            query.UserId, _clock.UtcNow, cancellationToken);

        return Result.Success<IReadOnlyList<SessionDto>>(
            families.Select(f => SessionMapping.ToDto(f, query.CurrentFamilyId)).ToList());
    }
}
