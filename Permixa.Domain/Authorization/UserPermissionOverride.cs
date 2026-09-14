using Permixa.Domain.Common;

namespace Permixa.Domain.Authorization;

/// <summary>
/// Fine-grained per-user permission override (Allow or Deny).
/// Absence of an override means Inherit from role permissions.
/// A user conceptually has at most one override per permission.
/// </summary>
public sealed class UserPermissionOverride
{
    private UserPermissionOverride(
        Guid id,
        Guid userId,
        Guid permissionId,
        PermissionEffect effect,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        Id = id;
        UserId = userId;
        PermissionId = permissionId;
        Effect = effect;
        CreatedAtUtc = createdAtUtc;
        UpdatedAtUtc = updatedAtUtc;
    }

    public Guid Id { get; }

    public Guid UserId { get; }

    public Guid PermissionId { get; }

    public PermissionEffect Effect { get; private set; }

    public DateTime CreatedAtUtc { get; }

    public DateTime UpdatedAtUtc { get; private set; }

    public static UserPermissionOverride Create(
        Guid userId,
        Guid permissionId,
        PermissionEffect effect,
        Guid? id = null,
        DateTime? createdAtUtc = null)
    {
        if (userId == Guid.Empty)
            throw new ArgumentException("User id cannot be empty.", nameof(userId));

        if (permissionId == Guid.Empty)
            throw new ArgumentException("Permission id cannot be empty.", nameof(permissionId));

        EnsureValidEffect(effect);

        var now = createdAtUtc ?? DateTime.UtcNow;
        return new UserPermissionOverride(
            id ?? Guid.NewGuid(),
            userId,
            permissionId,
            effect,
            now,
            now);
    }

    public static UserPermissionOverride Reconstitute(
        Guid id,
        Guid userId,
        Guid permissionId,
        PermissionEffect effect,
        DateTime createdAtUtc,
        DateTime updatedAtUtc)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Override id cannot be empty.", nameof(id));

        EnsureValidEffect(effect);

        return new UserPermissionOverride(id, userId, permissionId, effect, createdAtUtc, updatedAtUtc);
    }

    /// <summary>
    /// Changes the override effect (Allow ↔ Deny).
    /// </summary>
    public void ChangeEffect(PermissionEffect effect, DateTime? updatedAtUtc = null)
    {
        EnsureValidEffect(effect);

        if (Effect == effect)
            return;

        Effect = effect;
        UpdatedAtUtc = updatedAtUtc ?? DateTime.UtcNow;
    }

    private static void EnsureValidEffect(PermissionEffect effect)
    {
        if (effect is not (PermissionEffect.Allow or PermissionEffect.Deny))
            throw new DomainException("Permission effect must be Allow or Deny.");
    }
}
