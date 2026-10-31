using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Domain;

public static class Booking
{
    private static UnitResult<ErrorList> Validate(Client client, Service service, Staff staff, Room? room)
    {
        return new ErrorListBuilder()
            .AddIf(client.IsArchived, new BookingError.ClientArchived(), "clientId")
            .AddIf(service.IsArchived, new BookingError.ServiceArchived(), "serviceId")
            .AddIf(!staff.ParticipatesInScheduling(), new BookingError.StaffUnavailable(), "staffId")
            .AddIf(room?.IsArchived == true, new BookingError.RoomArchived(), "roomId")
            .ToResult();
    }

    public static Result<Appointment, ErrorList> CreateAppointment(AppointmentId id, Client client, Service service, Staff staff, Room? room, DateTimeOffset startsAt)
    {
        var validation = Validate(client, service, staff, room);
        if (validation.IsFailure) return validation.Error;
        return AppointmentRules.Create(id, client.Id, service.Id, staff.Id, room?.Id, startsAt, service.Duration);
    }

    public static Result<Series, ErrorList> CreateSeries(SeriesId id, Client client, Service service, Staff staff, Room? room, DateOnly startsOn, SeriesEnd end, WeeklyRule weekly)
    {
        var validation = Validate(client, service, staff, room);
        if (validation.IsFailure) return validation.Error;
        return SeriesRules.Create(id, client.Id, service.Id, service.Duration, startsOn, end, new(weekly, staff.Id, room?.Id));
    }
}
