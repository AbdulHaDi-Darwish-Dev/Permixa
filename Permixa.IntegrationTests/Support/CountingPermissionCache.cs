using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Models;

namespace Permixa.IntegrationTests.Support;

public sealed class CountingPermissionCache : IPermissionCache
{
    private readonly IPermissionCache _inner;

    public CountingPermissionCache(IPermissionCache inner)
    {
        _inner = inner;
    }

    public int GetCount { get; private set; }

    public int SetCount { get; private set; }

    public async Task<AuthorizationSnapshot?> GetAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        GetCount++;
        return await _inner.GetAsync(userId, cancellationToken);
    }

    public async Task SetAsync(
        Guid userId,
        AuthorizationSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        SetCount++;
        await _inner.SetAsync(userId, snapshot, cancellationToken);
    }

    public Task RemoveAsync(Guid userId, CancellationToken cancellationToken = default) =>
        _inner.RemoveAsync(userId, cancellationToken);
}
