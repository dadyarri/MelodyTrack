using System.Collections.Immutable;
using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain;

public sealed record SchoolPublicContent
{
    internal SchoolPublicContent(string name, string description, string callNumber, ExternalLink messenger)
    { Name = name; Description = description; CallNumber = callNumber; Messenger = messenger; }

    public string Name { get; }

    public string Description { get; }

    public string CallNumber { get; }

    public ExternalLink Messenger { get; }
}

public sealed record TeacherPublicProfile
{
    internal TeacherPublicProfile(StaffId staffId, string name, ExternalLink? photo, string description, string experience,
        ImmutableArray<string> directions)
    { StaffId = staffId; Name = name; Photo = photo; Description = description; Experience = experience; Directions = directions; }

    public StaffId StaffId { get; }

    public string Name { get; }

    public ExternalLink? Photo { get; }

    public string Description { get; }

    public string Experience { get; }

    public ImmutableArray<string> Directions { get; }
}

public sealed record PublicService(ServiceId Id, string Name, LessonDuration Duration, decimal Price)
{

}

public sealed record CalendarEvent(AppointmentId AppointmentId, TimeInterval Period, string ServiceName, string TeacherName,
    string? RoomName, string? ClientName)
{

}

public static class ClientActivity
{
    public static Result<DateTimeOffset?, ErrorList> LastOrdinaryLessonEndedAt(ClientId clientId,
        IEnumerable<Appointment> appointments, IEnumerable<Service> services)
    {
        var catalogue = services.DistinctBy(item => item.Id).ToDictionary(item => item.Id);
        var results = appointments.Where(item => item.ClientId == clientId && !item.IsDeleted &&
            item.Status is AppointmentStatus.Completed or AppointmentStatus.Burned).ToArray();
        if (results.Any(item => !catalogue.ContainsKey(item.ServiceId))) return ErrorList.Single(new ModelError.NotFound(), "serviceId");
        return results.Where(item => !catalogue[item.ServiceId].IsConsultation).Select(item => (DateTimeOffset?)item.EndsAt).Max();
    }
}
