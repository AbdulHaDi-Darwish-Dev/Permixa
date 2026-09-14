using Permixa.Domain.Common;

namespace Permixa.Domain.Authorization;

/// <summary>
/// Durable global RBAC authorization version.
/// Incremented when shared role-permission configuration changes.
/// </summary>
public sealed class AuthorizationState
{
    public const int InitialRbacVersion = 1;

    /// <summary>
    /// Well-known singleton id for the global authorization state row.
    /// </summary>
    public static readonly Guid GlobalId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private AuthorizationState(Guid id, int rbacVersion)
    {
        Id = id;
        RbacVersion = rbacVersion;
    }

    public Guid Id { get; }

    public int RbacVersion { get; private set; }

    public static AuthorizationState CreateInitial(Guid? id = null)
    {
        return new AuthorizationState(id ?? GlobalId, InitialRbacVersion);
    }

    public static AuthorizationState Reconstitute(Guid id, int rbacVersion)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Authorization state id cannot be empty.", nameof(id));

        if (rbacVersion < InitialRbacVersion)
            throw new ArgumentOutOfRangeException(
                nameof(rbacVersion),
                "RBAC version cannot be less than the initial value.");

        return new AuthorizationState(id, rbacVersion);
    }

    /// <summary>
    /// Increments the global RBAC version when shared authorization structure changes.
    /// </summary>
    public int IncrementRbacVersion()
    {
        if (RbacVersion == int.MaxValue)
            throw new DomainException("RBAC version overflow.");

        RbacVersion++;
        return RbacVersion;
    }
}
