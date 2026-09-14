using System.Collections.Concurrent;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Models;

namespace Permixa.Infrastructure.Caching;

/// <summary>
/// Process-local authorization snapshot cache for simple hosts, tests, and non-Redis environments.
/// For distributed production caching, install Permixa.Caching.Redis and call AddPermixaRedisAuthorizationCache.
/// </summary>
public sealed class MemoryPermissionCache : IPermissionCache
{
    private readonly ConcurrentDictionary<Guid, AuthorizationSnapshot> _store = new();

    public Task<AuthorizationSnapshot?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        _store.TryGetValue(userId, out var snapshot);
        return Task.FromResult(snapshot);
    }

    public Task SetAsync(
        Guid userId,
        AuthorizationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        _store[userId] = snapshot;
        return Task.CompletedTask;
    }

    public Task RemoveAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        _store.TryRemove(userId, out _);
        return Task.CompletedTask;
    }
}
