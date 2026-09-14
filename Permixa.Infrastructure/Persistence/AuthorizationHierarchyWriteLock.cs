using Permixa.Application.Authorization.Abstractions;
using Permixa.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence;

/// <summary>
/// Serializes hierarchy writes with a SQL Server update lock on the singleton AuthorizationState row.
/// Must be used inside an open SQL transaction so the lock is held until commit/rollback.
/// Callers must re-read role tiers after <see cref="AcquireAsync"/> returns.
/// </summary>
public sealed class AuthorizationHierarchyWriteLock : IAuthorizationHierarchyWriteLock
{
    private readonly ApplicationDbContext _db;

    public AuthorizationHierarchyWriteLock(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<AuthorizationState?> AcquireAsync(CancellationToken cancellationToken = default)
    {
        if (_db.Database.CurrentTransaction is null)
        {
            throw new InvalidOperationException(
                "Hierarchy write lock requires an open SQL transaction.");
        }

        var id = AuthorizationState.GlobalId;
        await _db.Database.ExecuteSqlAsync(
            $"SELECT [Id] FROM [AuthorizationStates] WITH (UPDLOCK, ROWLOCK) WHERE [Id] = {id}",
            cancellationToken);

        foreach (var tracked in _db.ChangeTracker.Entries<AuthorizationState>().ToList())
            tracked.State = EntityState.Detached;

        return await _db.AuthorizationStates
            .FirstOrDefaultAsync(state => state.Id == id, cancellationToken);
    }
}
