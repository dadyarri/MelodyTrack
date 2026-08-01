using MelodyTrack.Backend.Api.Clients;
using MelodyTrack.Backend.Data.Enums;
using Shouldly;

namespace MelodyTrack.Backend.Tests;

public class ClientLifecycleResolverTests
{
    [Theory]
    [InlineData(false, false, false, false, true, ClientLifecycleStatus.Lead)]
    [InlineData(false, false, true, false, false, ClientLifecycleStatus.ThinkingLead)]
    [InlineData(false, false, true, true, false, ClientLifecycleStatus.Client)]
    [InlineData(false, true, true, false, false, ClientLifecycleStatus.Client)]
    [InlineData(false, false, false, false, false, ClientLifecycleStatus.Client)]
    [InlineData(true, true, true, true, true, ClientLifecycleStatus.ClosedLead)]
    public void Resolve_AppliesLifecycleRulesAndManualClosurePrecedence(
        bool isLeadClosed,
        bool hasFutureRegularAppointment,
        bool hasCompletedConsultation,
        bool hasPaidAppointmentAfterConsultation,
        bool hasPlannedConsultation,
        ClientLifecycleStatus expected)
    {
        ClientLifecycleResolver.Resolve(
                isLeadClosed,
                hasFutureRegularAppointment,
                hasCompletedConsultation,
                hasPaidAppointmentAfterConsultation,
                hasPlannedConsultation)
            .ShouldBe(expected);
    }
}
