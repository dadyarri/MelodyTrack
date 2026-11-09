using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

// Finite actions from the accepted server contract. Special God Mode and private practice are not grantable actions.
public enum StaffPermission
{
    ClientsRead, ClientsWrite, ClientNotesRead, ClientNotesWrite,
    ScheduleRead, SchedulePlan, AppointmentResults,
    ClientAbsences, OwnAbsenceRequests, StaffAbsenceApproval, WorkingSchedulePrepare, WorkingScheduleApproval,
    FinanceRead, PaymentRecord, PaymentCorrect, RefundRemaining, RefundAll,
    ExpensesRead, ExpensesRecord, ExpensesCorrect, ExpensesDelete, ExpensesExport,
    TasksRead, TasksProcess, TaskRulesManage, CataloguesManage, StaffManage, PoliciesManage,
    TrainingRead, TrainingProgress
}

public enum PermissionDataScope { Own, Available }
public enum PermissionTimeScope { Today, CurrentMonth, Any }

public sealed record PermissionGrant(bool Allowed, PermissionDataScope DataScope, PermissionTimeScope TimeScope)
{
    public static PermissionGrant Denied { get; } = new(false, PermissionDataScope.Available, PermissionTimeScope.Any);

    public UnitResult<ErrorList> Validate() => Enum.IsDefined(DataScope) && Enum.IsDefined(TimeScope)
        ? UnitResult.Success<ErrorList>()
        : UnitResult.Failure(ErrorList.Single(new ModelError.InvalidValue(), "scope"));
}

public sealed record StaffPermissions(ImmutableDictionary<StaffPermission, PermissionGrant> Grants)
{
    public PermissionGrant For(StaffPermission permission) => Grants.TryGetValue(permission, out var grant) && grant is not null ? grant : PermissionGrant.Denied;

    public UnitResult<ErrorList> Validate() => Grants.Any(pair => !Enum.IsDefined(pair.Key) || pair.Value is null || pair.Value.Validate().IsFailure)
        ? UnitResult.Failure(ErrorList.Single(new ModelError.InvalidValue(), "permissions")) : UnitResult.Success<ErrorList>();

    public static StaffPermissions Resolve(StaffRole role, IReadOnlyDictionary<StaffPermission, PermissionGrant> rolePolicy,
        IReadOnlyDictionary<StaffPermission, PermissionGrant> personal)
    {
        var grants = ImmutableDictionary.CreateBuilder<StaffPermission, PermissionGrant>();
        foreach (var permission in Enum.GetValues<StaffPermission>())
        {
            // The superuser role is immutable. It still cannot bypass subject invariants outside this permission set.
            if (role == StaffRole.Superuser) grants[permission] = new(true, PermissionDataScope.Available, PermissionTimeScope.Any);
            else if (personal.TryGetValue(permission, out var local)) grants[permission] = local;
            else grants[permission] = rolePolicy.GetValueOrDefault(permission, PermissionGrant.Denied);
        }
        return new(grants.ToImmutable());
    }
}

public static class InitialPermissionPolicies
{
    // Defaults are for trusted pure operations and initial database setup, never a fallback after a failed current-state lookup.
    public static StaffPermissions ForRole(StaffRole role)
    {
        var grants = ImmutableDictionary.CreateBuilder<StaffPermission, PermissionGrant>();
        void Allow(StaffPermission permission, PermissionDataScope data = PermissionDataScope.Available,
            PermissionTimeScope time = PermissionTimeScope.Any) => grants[permission] = new(true, data, time);
        if (role == StaffRole.Superuser) return StaffPermissions.Resolve(role, grants.ToImmutable(), grants.ToImmutable());
        if (role is not (StaffRole.Administrator or StaffRole.Teacher)) return new(grants.ToImmutable());
        Allow(StaffPermission.ClientNotesRead);
        Allow(StaffPermission.OwnAbsenceRequests, PermissionDataScope.Own);
        Allow(StaffPermission.ClientAbsences);
        Allow(StaffPermission.TrainingRead, PermissionDataScope.Own);
        Allow(StaffPermission.TrainingProgress, PermissionDataScope.Own);
        if (role == StaffRole.Administrator)
        {
            foreach (var permission in new[] { StaffPermission.ClientsRead, StaffPermission.ClientsWrite,
                StaffPermission.ClientNotesWrite, StaffPermission.ScheduleRead, StaffPermission.SchedulePlan,
                StaffPermission.FinanceRead, StaffPermission.RefundRemaining, StaffPermission.TasksRead, StaffPermission.TasksProcess }) Allow(permission);
            Allow(StaffPermission.AppointmentResults, time: PermissionTimeScope.CurrentMonth);
            foreach (var permission in new[] { StaffPermission.PaymentRecord, StaffPermission.PaymentCorrect,
                StaffPermission.ExpensesRead, StaffPermission.ExpensesRecord, StaffPermission.ExpensesCorrect }) Allow(permission, time: PermissionTimeScope.Today);
        }
        else
        {
            Allow(StaffPermission.ClientsRead, PermissionDataScope.Own);
            Allow(StaffPermission.ScheduleRead, PermissionDataScope.Own);
            Allow(StaffPermission.WorkingSchedulePrepare, PermissionDataScope.Own);
        }
        return new(grants.ToImmutable());
    }
}
