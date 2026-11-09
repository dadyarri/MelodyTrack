using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class ConsultationFollowUpTests
{
    private static Service ConsultationService => Ok(ServiceRules.Create(new(Guid(300)), "Консультация", Duration(60), true,
        Ok(ServicePrice.Create(Now, 0))));

    private static Appointment Consultation => Ok(AppointmentRules.Create(new(Guid(301)), TestData.ClientId,
        ConsultationService.Id, TestData.StaffId, null, Utc(3, 15), Duration(60)));

    [Theory]
    [InlineData(AppointmentStatus.Completed, true)]
    [InlineData(AppointmentStatus.Burned, false)]
    [InlineData(AppointmentStatus.Scheduled, false)]
    [InlineData(AppointmentStatus.Cancelled, false)]
    public void ShouldStop_OrdinaryAppointment_RequiresCompletedResult(AppointmentStatus status, bool expected)
    {
        var scheduled = MakeAppointment(302, Utc(5, 15));
        var ordinary = status switch
        {
            AppointmentStatus.Completed => Ok(scheduled.Complete(Utc(5, 17))),
            AppointmentStatus.Burned => Ok(scheduled.Burn(Utc(5, 17))),
            AppointmentStatus.Cancelled => Ok(scheduled.Cancel()),
            _ => scheduled
        };

        var result = Ok(ConsultationFollowUpRules.ShouldStop(Consultation, ConsultationService, [ordinary], [MakeService()]));

        Assert.Equal(expected, result);
    }

    [Fact]
    public void ShouldStop_CompletedConsultation_DoesNotStopFollowUp()
    {
        var another = Ok(AppointmentRules.Create(new(Guid(302)), TestData.ClientId, ConsultationService.Id,
            TestData.StaffId, null, Utc(5, 15), Duration(60)));

        Assert.False(Ok(ConsultationFollowUpRules.ShouldStop(Consultation, ConsultationService,
            [Ok(another.Complete(Utc(5, 17)))], [ConsultationService])));
    }

    [Fact]
    public void ShouldStop_CompletedBeforeConsultation_DoesNotStopNewFollowUp()
    {
        var earlier = Ok(MakeAppointment(302, Utc(2, 15)).Complete(Utc(2, 17)));

        Assert.False(Ok(ConsultationFollowUpRules.ShouldStop(Consultation, ConsultationService, [earlier], [MakeService()])));
    }

    [Fact]
    public void ShouldStop_CompletedAppointmentMissingService_ReportsMissingFacts()
    {
        var completed = Ok(MakeAppointment(302, Utc(5, 15)).Complete(Utc(5, 17)));

        AssertError(new ModelError.NotFound(), ConsultationFollowUpRules.ShouldStop(Consultation, ConsultationService, [completed], []));
    }
}
