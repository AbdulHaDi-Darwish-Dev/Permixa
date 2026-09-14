using Permixa.Application.Audit;
using Permixa.Application.Audit.Abstractions;
using Permixa.Application.Authorization.Abstractions;
using Permixa.Application.Authorization.Permissions.Models;
using Permixa.Application.Common.Abstractions;
using Permixa.Application.Common.Results;
using Permixa.Domain.Authorization;

namespace Permixa.Application.Authorization.Permissions.Create;

public sealed class CreatePermissionUseCase
{
    private readonly IEffectivePermissionService _effectivePermissionService;
    private readonly IPermissionRepository _permissionRepository;
    private readonly IIamAuditSink _audit;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public CreatePermissionUseCase(
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
        CreatePermissionCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        var permissionCheck = await _effectivePermissionService.HasPermissionAsync(
            command.ActorUserId,
            IamPermissions.Permissions.Create,
            cancellationToken);

        if (permissionCheck.IsFailure)
            return Result.Failure<PermissionDto>(permissionCheck.Error!);

        if (!permissionCheck.Value)
            return Result.Failure<PermissionDto>(AuthorizationErrors.MissingManagePermission);

        var validation = PermissionName.Validate(command.Name);
        if (validation.IsFailure)
            return Result.Failure<PermissionDto>(validation.Error!);

        var name = PermissionName.Normalize(command.Name);

        if (await _permissionRepository.ExistsByNameAsync(name, cancellationToken))
            return Result.Failure<PermissionDto>(AuthorizationErrors.PermissionAlreadyExists);

        var now = _clock.UtcNow;
        var permission = Permission.Create(name, command.Description, createdAtUtc: now);
        await _permissionRepository.AddAsync(permission, cancellationToken);
        await _audit.WriteAsync(
            IamAuditEvent.Success(
                IamAuditEvents.Permissions.Created,
                now,
                actorUserId: command.ActorUserId,
                targetPermissionId: permission.Id),
            cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return Result.Success(PermissionMapping.ToDto(permission));
    }
}
