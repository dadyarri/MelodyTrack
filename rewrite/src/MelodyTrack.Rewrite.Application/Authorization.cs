using MelodyTrack.Rewrite.Domain;

namespace MelodyTrack.Rewrite.Application;

public static class Authorization
{
    public static bool IsSuperuser(this Actor actor) => actor is Actor.Staff { Role: StaffRole.Superuser };
    public static bool IsReception(this Actor actor) => actor is Actor.Staff { Role: StaffRole.Superuser or StaffRole.Administrator };

    public static bool HasPermission(this Actor actor, StaffPermission permission, StaffId? subjectOwner = null,
        DateTimeOffset? recordAt = null, DateTimeOffset? now = null)
    {
        if (!Enum.IsDefined(permission) || actor is not Actor.Staff staff || staff.Id.Value == Guid.Empty || !Enum.IsDefined(staff.Role)) return false;
        if (permission == StaffPermission.PoliciesManage && staff.Role != StaffRole.Superuser) return false;
        var grant = (staff.Permissions ?? InitialPermissionPolicies.ForRole(staff.Role)).For(permission);
        if (!grant.Allowed || grant.Validate().IsFailure) return false;
        if (grant.DataScope == PermissionDataScope.Own && subjectOwner != staff.Id) return false;
        return grant.TimeScope switch
        {
            PermissionTimeScope.Any => true,
            PermissionTimeScope.Today => recordAt is { } record && now is { } current && SchoolTime.Date(record) == SchoolTime.Date(current),
            PermissionTimeScope.CurrentMonth => recordAt is { } record && now is { } current && SchoolTime.SameMonth(record, current),
            _ => false
        };
    }
}

public static class RecordEditing
{
    // This constrains time; it does not grant a role permission to edit a financial record.
    public static bool IsWithinAllowedMonth(StaffRole role, DateTimeOffset recordAt, DateTimeOffset now) =>
        role == StaffRole.Superuser || SchoolTime.SameMonth(recordAt, now);
}
