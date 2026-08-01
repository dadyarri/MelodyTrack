using MelodyTrack.Backend.Data.Enums;

namespace MelodyTrack.Backend.Api.Clients;

public static class ClientLifecycleResolver
{
    public static ClientLifecycleStatus Resolve(
        bool isLeadClosed,
        bool hasFutureRegularAppointment,
        bool hasCompletedConsultation,
        bool hasPaidAppointmentAfterConsultation,
        bool hasPlannedConsultation)
    {
        if (isLeadClosed)
        {
            return ClientLifecycleStatus.ClosedLead;
        }

        if (hasFutureRegularAppointment)
        {
            return ClientLifecycleStatus.Client;
        }

        if (hasCompletedConsultation && !hasPaidAppointmentAfterConsultation)
        {
            return ClientLifecycleStatus.ThinkingLead;
        }

        return hasPlannedConsultation ? ClientLifecycleStatus.Lead : ClientLifecycleStatus.Client;
    }
}
