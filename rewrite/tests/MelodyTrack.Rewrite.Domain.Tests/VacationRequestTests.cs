using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class VacationRequestTests
{
    private static VacationRequest Request() => Ok(VacationRequestRules.Create(new(Guid(50)),
        new VacationSubject.Staff(TestData.StaffId), Period(Utc(5, 15), Utc(6, 15)), "Семейные обстоятельства"));

    [Fact]
    public void Create_ClientSubject_RejectsRequest() => AssertError(new ModelError.Forbidden(),
        VacationRequestRules.Create(new(Guid(50)), new VacationSubject.Client(TestData.ClientId), Period(Utc(5, 15), Utc(6, 15)), "Отпуск"));

    [Theory]
    [InlineData(null)]
    [InlineData("  ")]
    public void Create_MissingComment_RejectsRequest(string? comment) => AssertError(new CatalogueError.Required("comment"),
        VacationRequestRules.Create(new(Guid(50)), new VacationSubject.Staff(TestData.StaffId), Period(Utc(5, 15), Utc(6, 15)), comment));

    [Fact]
    public void Edit_PendingRequest_ChangesPeriodAndRequiresComment()
    {
        var period = Period(Utc(6, 15), Utc(7, 15));

        var changed = Ok(Request().Edit(period, "Комментарий"));

        Assert.Equal(period, changed.Period);
        Assert.IsType<VacationRequestStatus.Pending>(changed.Status);
        Assert.Null(changed.Target);
        AssertError(new CatalogueError.Required("comment"), changed.Edit(period, null));
    }

    [Fact]
    public void Approve_PendingRequest_PreservesRequestComment()
    {
        var original = Request();
        var id = new VacationId(Guid(51));

        var approved = Ok(original.Approve(id, null, []));

        Assert.Equal(new VacationRequestStatus.Approved(id), approved.Request.Status);
        Assert.Equal(original.Id, approved.Vacation.RequestId);
        Assert.Equal(original.Comment, approved.Vacation.Comment);
        Assert.Equal(original.Period, approved.Vacation.Period);
        Assert.Null(approved.Request.ResponseComment);
        AssertError(new VacationError.RequestAlreadyDecided(), approved.Request.Withdraw());
        AssertError(new VacationError.RequestAlreadyDecided(), approved.Request.Edit(original.Period, "Комментарий"));
    }

    [Fact]
    public void Approve_ConflictingAppointment_KeepsRequestPending()
    {
        var request = Request();
        var appointment = MakeAppointment(55, Utc(5, 15));

        AssertError(new AbsenceError.ConflictingAppointments(), request.Approve(new(Guid(51)), null, [appointment]));
        Assert.IsType<VacationRequestStatus.Pending>(request.Status);
        Assert.Equal(AppointmentStatus.Scheduled, appointment.Status);
    }

    [Fact]
    public void Withdraw_PendingRequest_PreventsLaterApproval() => AssertError(new VacationError.RequestAlreadyDecided(),
        Ok(Request().Withdraw()).Approve(new(Guid(51)), null, []));

    [Fact]
    public void Reject_PendingRequest_KeepsResponseComment()
    {
        var rejected = Ok(Request().Reject("Выберите другие даты"));

        Assert.IsType<VacationRequestStatus.Rejected>(rejected.Status);
        Assert.Equal("Выберите другие даты", rejected.ResponseComment);
    }

    [Fact]
    public void Approve_ChangeRequest_RequiresCurrentVacation()
    {
        var vacation = Ok(Request().Approve(new(Guid(51)), null, [])).Vacation;
        var request = Ok(VacationRequestRules.CreateChange(new(Guid(52)), vacation, Period(Utc(5, 15), Utc(6, 14)), "Раннее завершение"));

        AssertError(new VacationError.CurrentVacationRequired(), request.Approve(vacation.Id, null, []));
        AssertError(new VacationError.WrongVacation(), request.Approve(new(Guid(53)), null, []));
        Assert.IsType<VacationRequestStatus.Pending>(request.Status);
    }

    [Fact]
    public void ApproveChange_ActiveVacation_PreservesOrigin()
    {
        var vacation = Ok(Request().Approve(new(Guid(51)), null, [])).Vacation;
        var period = Period(Utc(5, 15).AddMinutes(30), Utc(6, 14));
        var request = Ok(VacationRequestRules.CreateChange(new(Guid(52)), vacation, period, "Раннее завершение"));

        var approval = Ok(request.ApproveChange(vacation, "  Согласовано  ", []));

        Assert.Equal(vacation.Id, approval.Vacation.Id);
        Assert.Equal(vacation.Subject, approval.Vacation.Subject);
        Assert.Equal(vacation.RequestId, approval.Vacation.RequestId);
        Assert.Equal(vacation.Comment, approval.Vacation.Comment);
        Assert.Equal(period, approval.Vacation.Period);
        Assert.Equal("Согласовано", approval.Request.ResponseComment);
        AssertError(new VacationError.RequestAlreadyDecided(), approval.Request.ApproveChange(approval.Vacation, null, []));
    }

    [Fact]
    public void ApproveChange_DeletedVacation_RejectsApproval()
    {
        var vacation = Ok(Request().Approve(new(Guid(51)), null, [])).Vacation;
        var request = Ok(VacationRequestRules.CreateChange(new(Guid(52)), vacation, Period(Utc(5, 15), Utc(6, 14)), "Комментарий"));

        AssertError(new VacationError.VacationDeleted(), request.ApproveChange(vacation.Remove(), null, []));
    }

    [Fact]
    public void ApproveChange_DifferentVacation_RejectsApproval()
    {
        var vacation = Ok(Request().Approve(new(Guid(51)), null, [])).Vacation;
        var request = Ok(VacationRequestRules.CreateChange(new(Guid(52)), vacation, Period(Utc(5, 15), Utc(6, 14)), "Комментарий"));
        var other = MakeVacation(53, vacation.Subject, Utc(5, 15), Utc(7, 15));

        AssertError(new VacationError.WrongVacation(), request.ApproveChange(other, null, []));
    }

    [Fact]
    public void ApproveChange_NewVacationRequest_RejectsApproval()
    {
        var vacation = Ok(Request().Approve(new(Guid(51)), null, [])).Vacation;

        AssertError(new VacationError.WrongVacation(), Request().ApproveChange(vacation, null, []));
    }
}
