using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ValidationTests
{
    [Fact]
    public void Create_MissingFirstAndLastNames_ReturnsBothFieldErrors()
    {
        var result = PersonName.Create(" ", null, null);
        Assert.True(result.IsFailure);
        Assert.Collection(result.Error.Issues,
            issue => { Assert.Equal(new CatalogueError.Required("firstName"), issue.Error); Assert.Equal("firstName", issue.Field); },
            issue => { Assert.Equal(new CatalogueError.Required("lastName"), issue.Error); Assert.Equal("lastName", issue.Field); });
    }

    [Fact]
    public void Create_ArchivedKindAndInvalidContact_ReturnsBothIndependentErrors()
    {
        var result = ClientContactRules.Create(new(Guid(30)), PhoneKind.Archive(), "wrong", null, true);
        AssertError(new ClientError.ContactKindArchived(), result);
        AssertError(new ClientError.InvalidContactValue(), result);
        Assert.Equal(2, result.Error.Issues.Length);
    }

    [Fact]
    public void Create_MissingPrimaryInSeveralKinds_ReturnsEachKind()
    {
        var email = Ok(ClientContactRules.Create(new(Guid(33)), Ok(ContactKindRules.Create(new(Guid(32)), "Email", ContactFormat.Email)), "test@example.com", null, false));
        var result = ClientRules.Create(TestData.ClientId, Profile, [Contact(30, false), email], TestData.Now);
        AssertError(new ClientError.PrimaryContactRequired(PhoneKind.Id), result);
        AssertError(new ClientError.PrimaryContactRequired(email.KindId), result);
        Assert.Equal(2, result.Error.Issues.Length);
    }

    [Fact]
    public void Archive_SeveralDependencies_ReturnsAllBlockers()
    {
        var result = MakeStaff().Archive(new(true, true, false), true);
        AssertError(new CatalogueError.FutureAppointmentsExist(), result);
        AssertError(new CatalogueError.ActiveSeriesExist(), result);
        AssertError(new CatalogueError.SoleCourseTeacher(), result);
        Assert.Equal(3, result.Error.Issues.Length);
    }

    [Fact]
    public void CreateAppointment_SeveralUnavailableReferences_ReturnsAllBlockers()
    {
        var room = Ok(RoomRules.Create(TestData.RoomId, "Зал", null)).Archive();
        var result = Booking.CreateAppointment(new(Guid(103)), Ok(MakeClient().Archive(NoUsage)), Ok(MakeService().Archive(NoUsage)),
            MakeStaff().SetParticipation(false), room, Utc(5, 15));
        Assert.Equal(4, result.Error.Issues.Length);
        AssertError(new BookingError.ClientArchived(), result);
        AssertError(new BookingError.ServiceArchived(), result);
        AssertError(new BookingError.StaffUnavailable(), result);
        AssertError(new BookingError.RoomArchived(), result);
    }

    [Fact]
    public void Approve_ConflictingAppointmentsAndSeries_ReturnsBothBlockers()
    {
        var schedule = Ok(WorkingScheduleRules.Create(new(Guid(60)), TestData.StaffId, Date(5), [], [], []));
        var result = Ok(schedule.Submit()).Approve(StaffRole.Superuser, true, true);
        AssertError(new WorkingScheduleError.ConflictingAppointments(), result);
        AssertError(new WorkingScheduleError.ConflictingSeries(), result);
        Assert.Equal(2, result.Error.Issues.Length);
    }

    [Fact]
    public void SelectPrimary_UnknownContact_StopsBeforeDependentWork()
    {
        var client = MakeClient();
        var result = client.SelectPrimary(new(Guid(99)));
        AssertError(new ClientError.UnknownContact(), result);
        Assert.Single(result.Error.Issues);
        Assert.True(Assert.Single(client.Contacts).IsPrimary);
    }

}
