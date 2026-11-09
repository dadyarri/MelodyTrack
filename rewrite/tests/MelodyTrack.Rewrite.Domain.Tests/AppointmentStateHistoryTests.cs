using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class AppointmentStateHistoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Complete_PastAppointmentWithoutComment_RejectsChange(string? comment)
    {
        var planned = MakeAppointment(90, Utc(5, 15));

        var result = planned.Complete(StaffRole.Superuser, Utc(6, 16), comment, TestData.StaffId);

        AssertError(new CatalogueError.Required("comment"), result);
        Assert.Equal(AppointmentStatus.Scheduled, planned.Status);
        Assert.Empty(planned.StateHistory);
    }

    [Fact]
    public void Complete_PastAppointmentWithoutAuthor_RejectsChange() => AssertError(new ModelError.InvalidValue(),
        MakeAppointment(90, Utc(5, 15)).Complete(StaffRole.Superuser, Utc(6, 16), "Проведено"));

    [Fact]
    public void ClearResult_PastAppointment_RecordsBothChangesAndRequiresExplanation()
    {
        var planned = MakeAppointment(90, Utc(5, 15));
        var completed = Ok(planned.Complete(StaffRole.Superuser, Utc(6, 16), "  Проведено  ", TestData.StaffId));

        AssertError(new CatalogueError.Required("comment"), completed.ClearResult(StaffRole.Superuser, Utc(6, 17)));
        var cleared = Ok(completed.ClearResult(StaffRole.Superuser, Utc(6, 17).ToOffset(TimeSpan.FromHours(3)), "Ошибка результата", TestData.StaffId));

        Assert.Equal(2, cleared.StateHistory.Length);
        Assert.Equal(new AppointmentStateChange(TestData.StaffId, Utc(6, 16), AppointmentStatus.Scheduled,
            AppointmentStatus.Completed, false, false, "Проведено"), cleared.StateHistory[0]);
        Assert.Equal(new AppointmentStateChange(TestData.StaffId, Utc(6, 17), AppointmentStatus.Completed,
            AppointmentStatus.Scheduled, false, false, "Ошибка результата"), cleared.StateHistory[1]);
        Assert.Equal(TimeSpan.Zero, cleared.StateHistory[1].OccurredAt.Offset);
        Assert.Equal(completed, Ok(completed.Complete(StaffRole.Superuser, Utc(6, 17))));
    }

    [Fact]
    public void Cancel_PastAppointment_RequiresCommentAndRecordsManualCancellation()
    {
        var planned = MakeAppointment(90, Utc(5, 15));

        AssertError(new CatalogueError.Required("comment"), planned.Cancel(StaffRole.Superuser, Utc(6, 17)));
        var cancelled = Ok(planned.Cancel(StaffRole.Superuser, Utc(6, 17), "Исправление отсутствия", TestData.StaffId));

        Assert.Equal(AppointmentStatus.Cancelled, Assert.Single(cancelled.StateHistory).After);
    }

    [Fact]
    public void Delete_PastAppointment_RecordsDeletionWithoutChangingStatus()
    {
        var planned = MakeAppointment(90, Utc(5, 15));

        var deleted = Ok(planned.Delete(StaffRole.Superuser, Utc(6, 17), "Дубликат", TestData.StaffId));

        var entry = Assert.Single(deleted.StateHistory);
        Assert.False(entry.WasDeleted);
        Assert.True(entry.IsDeleted);
        Assert.Equal(entry.Before, entry.After);
    }
}
