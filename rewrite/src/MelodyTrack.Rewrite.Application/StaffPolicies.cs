using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application;

public sealed class StaffPolicies(IStaffPolicyStore store)
{
    public Task<UnitResult<ErrorList>> SetRoleAsync(Guid commandId, Actor.Staff author, StaffRole role,
        StaffPermission permission, PermissionGrant grant, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanManage(author) || role == StaffRole.Superuser)
            return Task.FromResult(UnitResult.Failure(ErrorList.Single(new ModelError.Forbidden())));
        return store.SetRolePolicyAsync(commandId, author.Id, role, permission, grant, cancellationToken);
    }

    public Task<UnitResult<ErrorList>> SetPersonalAsync(Guid commandId, Actor.Staff author, StaffId target,
        StaffPermission permission, PermissionGrant? grant, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!CanManage(author)) return Task.FromResult(UnitResult.Failure(ErrorList.Single(new ModelError.Forbidden())));
        return store.SetPersonalPolicyAsync(commandId, author.Id, target, permission, grant, cancellationToken);
    }

    private static bool CanManage(Actor.Staff actor) => actor.Validate().IsSuccess && actor.IsSuperuser()
        && actor.HasPermission(StaffPermission.PoliciesManage);
}
