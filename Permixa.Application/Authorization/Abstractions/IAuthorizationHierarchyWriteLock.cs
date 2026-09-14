using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.Abstractions;

/// <summary>
/// Exclusive hierarchy-write lock on the singleton <see cref="AuthorizationState"/> row.
/// Must be acquired inside an open SQL transaction. Application code must not use SQL hints.
/// </summary>
public interface IAuthorizationHierarchyWriteLock
{
    Task<AuthorizationState?> AcquireAsync(CancellationToken cancellationToken = default);
}