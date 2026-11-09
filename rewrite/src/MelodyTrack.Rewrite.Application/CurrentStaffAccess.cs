using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Application;

public interface ICurrentStaffAccess
{
    // Returns current database state; identity alone does not grant any operation.
    Task<Result<Actor.Staff, ErrorList>> ResolveAsync(StaffId staffId, CancellationToken cancellationToken);
}

public interface IStaffPolicyStore : ICurrentStaffAccess
{
    Task<UnitResult<ErrorList>> SetRolePolicyAsync(Guid commandId, StaffId authorId, StaffRole role,
        StaffPermission permission, PermissionGrant grant, CancellationToken cancellationToken);
    // null means inherit the current role, not a snapshot of its old permissions.
    Task<UnitResult<ErrorList>> SetPersonalPolicyAsync(Guid commandId, StaffId authorId, StaffId targetId,
        StaffPermission permission, PermissionGrant? grant, CancellationToken cancellationToken);
}
