using Permixa.Application.Authorization.Abstractions;
using Permixa.Domain.Authorization;
using Permixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence.Repositories;

public sealed class AuthorizationStateRepository : IAuthorizationStateRepository
{
    private readonly ApplicationDbContext _db;

    public AuthorizationStateRepository(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task<AuthorizationState?> GetAsync(CancellationToken cancellationToken = default) =>
        // Tracked: authorization mutations call IncrementRbacVersion then SaveChanges once.
        _db.AuthorizationStates
            .FirstOrDefaultAsync(s => s.Id == AuthorizationState.GlobalId, cancellationToken);

    public Task AddAsync(AuthorizationState state, CancellationToken cancellationToken = default)
    {
        _db.AuthorizationStates.Add(state);
        return Task.CompletedTask;
    }
}
