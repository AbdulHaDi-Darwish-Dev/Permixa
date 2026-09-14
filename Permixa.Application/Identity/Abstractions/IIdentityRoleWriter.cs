namespace Permixa.Application.Identity.Abstractions;

public interface IIdentityRoleWriter
{
    Task<IdentityRoleMutationResult> CreateAsync(
        string name,
        int roleLevel,
        CancellationToken cancellationToken = default);

    Task<IdentityRoleMutationResult> RenameAsync(
        Guid roleId,
        string newName,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves every role currently at each listed level to level+1, optionally excluding one role.
    /// Apply weakest (largest) FromLevel first.
    /// </summary>
    Task ShiftTiersAsync(
        IReadOnlyCollection<int> fromLevels,
        Guid? excludeRoleId,
        CancellationToken cancellationToken = default);

    Task SetRoleLevelAsync(
        Guid roleId,
        int roleLevel,
        CancellationToken cancellationToken = default);

    Task<IdentityRoleMutationResult> DeleteAsync(
        Guid roleId,
        CancellationToken cancellationToken = default);
}

public sealed class IdentityRoleMutationResult
{
    private IdentityRoleMutationResult(
        bool succeeded,
        Guid? roleId,
        IdentityRoleMutationFailure? failure)
    {
        Succeeded = succeeded;
        RoleId = roleId;
        Failure = failure;
    }

    public bool Succeeded { get; }

    public Guid? RoleId { get; }

    public IdentityRoleMutationFailure? Failure { get; }

    public static IdentityRoleMutationResult Success(Guid roleId) =>
        new(true, roleId, null);

    public static IdentityRoleMutationResult Failed(IdentityRoleMutationFailure failure) =>
        new(false, null, failure);
}

public enum IdentityRoleMutationFailure
{
    DuplicateName,
    InvalidName,
    NotFound,
    Failed
}
