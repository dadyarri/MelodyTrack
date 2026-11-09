using System.Collections.Immutable;
using MelodyTrack.Rewrite.Application;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class StaffPermissionsTests
{
    [Fact]
    public void Resolve_RoleChanged_PreservesPersonalAllowAndDeny()
    {
        var role = ImmutableDictionary<StaffPermission, PermissionGrant>.Empty
            .Add(StaffPermission.ClientNotesWrite, new(false, PermissionDataScope.Available, PermissionTimeScope.Any))
            .Add(StaffPermission.ScheduleRead, new(true, PermissionDataScope.Available, PermissionTimeScope.Any));
        var personal = ImmutableDictionary<StaffPermission, PermissionGrant>.Empty
            .Add(StaffPermission.ClientNotesWrite, new(true, PermissionDataScope.Available, PermissionTimeScope.Any))
            .Add(StaffPermission.ScheduleRead, PermissionGrant.Denied);

        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Teacher, StaffPermissions.Resolve(StaffRole.Teacher, role, personal));

        Assert.True(actor.HasPermission(StaffPermission.ClientNotesWrite));
        Assert.False(actor.HasPermission(StaffPermission.ScheduleRead));
        Assert.False(actor.HasPermission(StaffPermission.ExpensesExport));
    }

    [Fact]
    public void HasPermission_OwnScope_DeniesOtherStaffSubject()
    {
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Teacher);

        Assert.True(actor.HasPermission(StaffPermission.ScheduleRead, TestData.StaffId));
        Assert.False(actor.HasPermission(StaffPermission.ScheduleRead, new StaffId(Guid(80))));
        Assert.False(actor.HasPermission(StaffPermission.ScheduleRead));
    }

    [Fact]
    public void HasPermission_TodayScope_UsesMoscowDate()
    {
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Administrator);

        Assert.True(actor.HasPermission(StaffPermission.PaymentRecord, recordAt: Utc(1, 21), now: Utc(2, 12)));
        Assert.False(actor.HasPermission(StaffPermission.PaymentRecord, recordAt: Utc(1, 20), now: Utc(2, 12)));
    }

    [Fact]
    public void HasPermission_CurrentEmptyDatabasePolicy_DoesNotFallBackToRoleDefault()
    {
        var actor = new Actor.Staff(TestData.StaffId, StaffRole.Administrator,
            StaffPermissions.Resolve(StaffRole.Administrator, ImmutableDictionary<StaffPermission, PermissionGrant>.Empty,
                ImmutableDictionary<StaffPermission, PermissionGrant>.Empty));

        Assert.False(actor.HasPermission(StaffPermission.ClientNotesWrite));
    }
}
