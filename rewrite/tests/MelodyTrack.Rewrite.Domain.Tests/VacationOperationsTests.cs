using MelodyTrack.Rewrite.Application.Operations;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class VacationOperationsTests
{
    private static Actor StaffActor => new Actor.Staff(TestData.StaffId, StaffRole.Teacher);
    private static Actor SuperActor => new Actor.Staff(new(Guid(300)), StaffRole.Superuser);

    [Fact]
    public void CreateClient_ClientActor_RejectsVacation() => AssertError(new ModelError.Forbidden(),
        VacationOperations.CreateClient(new(Guid(200)), TestData.ClientId, Period(Utc(5, 15), Utc(6, 15)), "Отпуск", [], new Actor.Client(TestData.ClientId)));

    [Fact]
    public void CreateClient_EmployeeWithComment_CreatesVacation()
    {
        var vacation = Ok(VacationOperations.CreateClient(new(Guid(200)), TestData.ClientId,
            Period(Utc(5, 15), Utc(6, 15)), "  Обсудить расписание  ", [], StaffActor));

        Assert.Equal("Обсудить расписание", vacation.Comment);
        Assert.Equal(new VacationSubject.Client(TestData.ClientId), vacation.Subject);
    }

    [Fact]
    public void CreateClient_ConflictingLesson_RejectsWithoutChangingLesson()
    {
        var appointment = MakeAppointment(201, Utc(5, 15));

        var result = VacationOperations.CreateClient(new(Guid(200)), TestData.ClientId,
            Period(Utc(5, 15), Utc(6, 15)), "Отпуск", [appointment], StaffActor);

        AssertError(new AbsenceError.ConflictingAppointments(), result);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Fact]
    public void CreateClient_MissingComment_RejectsVacation() => AssertError(new CatalogueError.Required("comment"),
        VacationOperations.CreateClient(new(Guid(200)), TestData.ClientId, Period(Utc(5, 15), Utc(6, 15)), null, [], StaffActor));

    [Fact]
    public void Restore_HistoricalVacationWithoutComment_PreservesMissingComment()
    {
        var restored = Ok(VacationRules.Restore(new(Guid(200)), new VacationSubject.Client(TestData.ClientId),
            Period(Utc(5, 15), Utc(6, 15)), null, null));

        Assert.Null(restored.Comment);
    }

    [Fact]
    public void Approve_OwnStaffRequest_RejectsEvenForSuperuser()
    {
        var request = Ok(VacationRequestOperations.Create(new(Guid(200)), Period(Utc(5, 15), Utc(6, 15)), "Отпуск", SuperActor));

        AssertError(new ModelError.Forbidden(), request.Approve(new(Guid(201)), null, [], SuperActor));
        Assert.IsType<VacationRequestStatus.Pending>(request.Status);
    }

    [Fact]
    public void Approve_OtherStaffRequest_AllowsSuperuser()
    {
        var request = Ok(VacationRequestOperations.Create(new(Guid(200)), Period(Utc(5, 15), Utc(6, 15)), "Отпуск", StaffActor));

        var result = Ok(request.Approve(new(Guid(201)), null, [], SuperActor));

        Assert.Equal("Отпуск", result.Vacation.Comment);
    }

    [Fact]
    public void ChangePeriod_PastStaffVacation_AllowsEmployeeCorrectionWithoutApprovingNewRequest()
    {
        var vacation = MakeVacation(200, new VacationSubject.Staff(new(Guid(201))), Utc(5, 15), Utc(6, 15));

        var changed = Ok(vacation.ChangePeriod(Utc(7, 15), Period(Utc(5, 15), Utc(6, 14)), [], StaffActor));

        Assert.Equal(Utc(6, 14), changed.Period.EndsAt);
        Assert.Equal(vacation.Id, changed.Id);
    }

    [Fact]
    public void ChangePeriod_PastStaffVacationExtendedIntoFuture_RequiresDecisionInsteadOfBypassingApproval()
    {
        var vacation = MakeVacation(200, new VacationSubject.Staff(TestData.StaffId), Utc(5, 15), Utc(6, 15));

        AssertError(new ModelError.DecisionRequired(), vacation.ChangePeriod(Utc(7, 15),
            Period(Utc(5, 15), Utc(8, 15)), [], StaffActor));
        Assert.Equal(Utc(6, 15), vacation.Period.EndsAt);
    }

    [Fact]
    public void CreateClient_InvalidStaffIdentity_RejectsVacation() => AssertError(new IdentifierError.Empty(),
        VacationOperations.CreateClient(new(Guid(200)), TestData.ClientId, Period(Utc(5, 15), Utc(6, 15)), "Отпуск", [],
            new Actor.Staff(new(System.Guid.Empty), StaffRole.Teacher)));

    [Fact]
    public void CreateSchoolClosure_ConflictingLesson_BlocksApproval()
    {
        var result = SchoolClosureOperations.Create(new(Guid(200)), Period(Utc(5, 15), Utc(6, 15)),
            "Ремонт", [MakeAppointment(201, Utc(5, 15))], SuperActor);

        AssertError(new AbsenceError.ConflictingAppointments(), result);
    }
}
