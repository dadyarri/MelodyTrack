using MelodyTrack.Rewrite.Domain.Scheduling;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class StaffTests
{
    [Fact]
    public void Block_ParticipatingTeacher_PreservesSchedulingParticipation()
    {
        var blocked = MakeStaff().Block();
        Assert.True(blocked.IsBlocked);
        Assert.True(blocked.ParticipatesInScheduling);
        Assert.Equal(TestData.StaffId, blocked.Id);
    }
    [Fact]
    public void SetParticipation_Superuser_DoesNotChangeRoleOrBlockAccount()
    {
        var staff = Ok(Staff.Create(TestData.StaffId, "Супер", StaffRole.Superuser, true)).SetParticipation(false);
        Assert.False(staff.ParticipatesInScheduling);
        Assert.False(staff.IsBlocked);
        Assert.Equal(StaffRole.Superuser, staff.Role);
    }
    [Fact]
    public void Archive_SoleCourseTeacher_RequiresReplacement() => AssertError(new CatalogueError.SoleCourseTeacher(), MakeStaff().Archive(NoUsage, true));
    [Fact]
    public void Archive_ActiveSeries_RequiresReplacement() => AssertError(new CatalogueError.ActiveSeriesExist(), MakeStaff().Archive(NoUsage with { HasActiveSeries = true }, false));
    [Fact]
    public void Archive_NoDependencies_PreservesIdentityAndExcludesFromScheduling()
    {
        var archived = Ok(MakeStaff().Archive(NoUsage, false));
        Assert.Equal(TestData.StaffId, archived.Id);
        Assert.True(archived.IsArchived);
        Assert.False(archived.ParticipatesInScheduling);
    }
    [Fact]
    public void Create_BlankName_RejectsStaff() => AssertError(new CatalogueError.Required("name"), Staff.Create(TestData.StaffId, "  ", StaffRole.Teacher, true));
}
