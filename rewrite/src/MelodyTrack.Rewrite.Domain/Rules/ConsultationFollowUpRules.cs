using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class ConsultationFollowUpRules
{
    // D-482 / BIZ-49: a burned or merely overdue ordinary appointment does not stop follow-up.
    public static Result<bool, ErrorList> ShouldStop(Appointment consultation, Service consultationService,
        IEnumerable<Appointment> appointments, IEnumerable<Service> services)
    {
        if (consultation.ServiceId != consultationService.Id || !consultationService.IsConsultation)
            return ErrorList.Single(new ModelError.InvalidValue(), "consultation");
        var catalogue = services.ToArray();
        if (catalogue.Select(service => service.Id).Distinct().Count() != catalogue.Length)
            return ErrorList.Single(new ModelError.Duplicate(), "services");
        var byId = catalogue.ToDictionary(service => service.Id);
        var missingService = false;
        foreach (var appointment in appointments.Where(appointment => appointment.ClientId == consultation.ClientId
            && !appointment.IsDeleted && appointment.Status == AppointmentStatus.Completed
            && appointment.StartsAt >= consultation.EndsAt))
        {
            if (!byId.TryGetValue(appointment.ServiceId, out var service)) missingService = true;
            else if (!service.IsConsultation) return true;
        }
        return missingService ? ErrorList.Single(new ModelError.NotFound(), "serviceId") : false;
    }
}
