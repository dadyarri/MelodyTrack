using CSharpFunctionalExtensions;
using MelodyTrack.Rewrite.Domain;
using MelodyTrack.Rewrite.Domain.Rules;

namespace MelodyTrack.Rewrite.Application.Operations;

public static class PublicServiceOperations
{
    public static Result<Service, ErrorList> SetPublicPrice(this Service service, decimal amount, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return ServiceRules.SetPublicPrice(service, amount);
    }
}

public static class SchoolPublicContentOperations
{
    public static Result<SchoolPublicContent, ErrorList> Create(string? name, string? description, string? callNumber, ExternalLink messenger, Actor actor)
    {
        if (!actor.IsSuperuser()) return ErrorList.Single(new ModelError.Forbidden());
        return SchoolPublicContentRules.Create(name, description, callNumber, messenger);
    }
}

public static class TeacherPublicProfileOperations
{
    public static Result<TeacherPublicProfile, ErrorList> Create(StaffId staffId, string? name, ExternalLink? photo, string? description, string? experience, IEnumerable<string> directions, Actor actor)
    {
        if (!actor.IsSuperuser() && !(actor is Actor.Staff { Role: StaffRole.Teacher } teacher && teacher.Id == staffId))
            return ErrorList.Single(new ModelError.Forbidden());
        return TeacherPublicProfileRules.Create(staffId, name, photo, description, experience, directions);
    }
}

public static class CalendarEventOperations
{
    public static Result<CalendarEvent, ErrorList> Create(CalendarSubscription subscription, Appointment appointment, Service service, Staff teacher, Room? room, string clientName)
    {
        if (!subscription.Matches(appointment)) return ErrorList.Single(new ModelError.Forbidden());
        if (service.Id != appointment.ServiceId || teacher.Id != appointment.StaffId || room?.Id != appointment.RoomId)
            return ErrorList.Single(new ModelError.InvalidValue(), "appointment");
        return new CalendarEvent(appointment.Id, appointment.Interval, service.PublicName, teacher.Name, room?.Name,
            subscription.Owner is Actor.Staff ? clientName : null);
    }
}
