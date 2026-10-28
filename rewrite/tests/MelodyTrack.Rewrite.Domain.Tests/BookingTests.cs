
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class BookingTests
{
    private static Service Service() => MakeService().ChangeDuration(Duration(45));
    private static Room Room() => Ok(global::MelodyTrack.Rewrite.Domain.Room.Create(TestData.RoomId, "Зал", null));
    [Fact]
    public void CreateAppointment_ActiveCatalogue_UsesServiceDuration()
    {
        var service = Service();
        var appointment = Ok(Booking.CreateAppointment(new(Guid(103)), MakeClient(), service, MakeStaff(), Room(), Utc(5, 15)));
        Assert.Equal(45, appointment.Duration.Minutes);
        Assert.Equal(service.Id, appointment.ServiceId);
    }
    [Fact]
    public void CreateSeries_ServiceLaterChangesDuration_PreservesFrozenDurationForFutureAppointments()
    {
        var service = Service();
        var series = Ok(Booking.CreateSeries(new(Guid(104)), MakeClient(), service, MakeStaff(), null, Date(5), new SeriesEnd.OpenEnded(), Weekly));
        var changed = service.ChangeDuration(Duration(90));
        var appointment = Ok(Appointment.CreateFromSeries(new(Guid(103)), series, SequenceNumber.First, Utc(5, 15)));
        Assert.Equal(90, changed.Duration.Minutes);
        Assert.Equal(45, series.Duration.Minutes);
        Assert.Equal(45, appointment.Duration.Minutes);
    }
    [Fact]
    public void CreateAppointment_ArchivedClient_RejectsNewBooking() => AssertError(new BookingError.ClientArchived(),
        Booking.CreateAppointment(new(Guid(103)), Ok(MakeClient().Archive(NoUsage)), Service(), MakeStaff(), null, Utc(5, 15)));
    [Fact]
    public void CreateSeries_ArchivedService_RejectsNewSeries() => AssertError(new BookingError.ServiceArchived(),
        Booking.CreateSeries(new(Guid(104)), MakeClient(), Ok(Service().Archive(NoUsage)), MakeStaff(), null, Date(5), new SeriesEnd.OpenEnded(), Weekly));
    [Fact]
    public void CreateAppointment_ArchivedRoom_RejectsAssignment() => AssertError(new BookingError.RoomArchived(),
        Booking.CreateAppointment(new(Guid(103)), MakeClient(), Service(), MakeStaff(), Room().Archive(), Utc(5, 15)));
    [Fact]
    public void CreateAppointment_NonParticipatingStaff_RejectsNewAssignment() => AssertError(new BookingError.StaffUnavailable(),
        Booking.CreateAppointment(new(Guid(103)), MakeClient(), Service(), MakeStaff().SetParticipation(false), null, Utc(5, 15)));
}
