using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Permissions.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;

namespace Permixa.Application.Authorization.Permissions.Update;

public sealed class UpdatePermissionDescriptionUseCase
{
    private readonly IEffectivePermissionService _effectivePermissionService;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public UpdatePermissionDescriptionUseCase(
        IEffectivePermissionService effectivePermissionService,
        IPermissionRepository permissionRepository,
        IIamAuditSink audit,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _effectivePermissionService = effectivePermissionService;
        _permissionRepository = permissionRepository;
        _audit = audit;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Result<PermissionDto>> ExecuteAsync(
        UpdatePermissionDescriptionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var permissionCheck = await _effectivePermissionService.HasPermissionAsync(
            command.ActorUserId,
            IamPermissions.Permissions.Update,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<PermissionDto>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<PermissionDto>(AuthorizationErrors.MissingManagePermission);

        if (command.PermissionId == Guid.Empty)
            return Result.Failure<PermissionDto>(AuthorizationErrors.PermissionNotFound);

        var permission = await _permissionRepository.GetByIdAsync(command.PermissionId, cancellationToken);
        if (permission is null)
            return Result.Failure<PermissionDto>(AuthorizationErrors.PermissionNotFound);

        var normalized = NormalizeDescription(command.Description);
        if (string.Equals(permission.Description, normalized, StringComparison.Ordinal))
            return Result.Success(PermissionMapping.ToDto(permission));

        var now = _clock.UtcNow;
        permission.UpdateDescription(command.Description, now);
        await _audit.WriteAsync(
            IamAuditEvent.Success(
                IamAuditEvents.Permissions.DescriptionUpdated,
                now,
                actorUserId: command.ActorUserId,
                targetPermissionId: permission.Id),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(PermissionMapping.ToDto(permission));
    }

    private static string? NormalizeDescription(string? description) =>
        string.IsNullOrWhiteSpace(description) ? null : description.Trim();
}
