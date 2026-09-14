using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Common.Abstractions;
using Permixa.Domain.Authorization;
using Microsoft.EntityFrameworkCore;

namespace Permixa.Infrastructure.Persistence;

/// <summary>
/// Idempotently ensures the global AuthorizationState row exists (RbacVersion = 1).
/// Does not overwrite an existing version.
/// </summary>
public interface IAuthorizationStateBootstrapper
{
    Task EnsureCreatedAsync(CancellationToken cancellationToken = default);
}

public sealed class AuthorizationStateBootstrapper : IAuthorizationStateBootstrapper
{
    private readonly ApplicationDbContext _db;
    private readonly IAuthorizationStateRepository _states;
    private readonly IUnitOfWork _unitOfWork;

    public AuthorizationStateBootstrapper(
        ApplicationDbContext db,
        IAuthorizationStateRepository states,
        IUnitOfWork unitOfWork)
    {
        _db = db;
        _states = states;
        _unitOfWork = unitOfWork;
    }

    public async Task EnsureCreatedAsync(CancellationToken cancellationToken = default)
    {
        var exists = await _db.AuthorizationStates
            .AnyAsync(s => s.Id == AuthorizationState.GlobalId, cancellationToken);

        if (exists)
            return;

        await _states.AddAsync(AuthorizationState.CreateInitial(), cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
