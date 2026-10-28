using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public static class Booking
{
    private static UnitResult<ErrorList> Validate(Client client, Service service, Staff staff, Room? room)
    {
        var errors = new List<DomainIssue>();
        if (client.IsArchived) errors.Add(new(new BookingError.ClientArchived(), "clientId"));
        if (service.IsArchived) errors.Add(new(new BookingError.ServiceArchived(), "serviceId"));
        if (!staff.ParticipatesInScheduling) errors.Add(new(new BookingError.StaffUnavailable(), "staffId"));
        if (room?.IsArchived == true) errors.Add(new(new BookingError.RoomArchived(), "roomId"));
        return errors.Count == 0 ? UnitResult.Success<ErrorList>() : UnitResult.Failure(ErrorList.From(errors));
    }

    public static Result<Appointment, ErrorList> CreateAppointment(AppointmentId id, Client client, Service service, Staff staff, Room? room, DateTimeOffset startsAt)
    {
        var validation = Validate(client, service, staff, room);
        if (validation.IsFailure) return validation.Error;
        return Appointment.Create(id, client.Id, service.Id, staff.Id, room?.Id, startsAt, service.Duration);
    }

    public static Result<Series, ErrorList> CreateSeries(SeriesId id, Client client, Service service, Staff staff, Room? room, DateOnly startsOn, SeriesEnd end, WeeklyRule weekly)
    {
        var validation = Validate(client, service, staff, room);
        if (validation.IsFailure) return validation.Error;
        return Series.Create(id, client.Id, service.Id, service.Duration, startsOn, end, new(weekly, staff.Id, room?.Id));
    }
}
