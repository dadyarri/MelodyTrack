using MelodyTrack.Rewrite.Application;
using MelodyTrack.Rewrite.Domain.Rules;
using Xunit;
using static MelodyTrack.Rewrite.Domain.Tests.TestData;

namespace MelodyTrack.Rewrite.Domain.Tests;

public sealed class VacationRequestTests
{
    private static VacationRequest Request() => Ok(VacationRequestRules.Create(new(Guid(50)), new VacationSubject.Client(TestData.ClientId), Period(Utc(5, 15), Utc(6, 15)), null));
    [Fact]
    public void Edit_PendingRequest_ChangesRequestedPeriodWithoutCreatingVacation()
    {
        var period = Period(Utc(6, 15), Utc(7, 15));
        var changed = Ok(Request().Edit(period, "Комментарий"));
        Assert.Equal(period, changed.Period);
        Assert.IsType<VacationRequestStatus.Pending>(changed.Status);
        Assert.Null(changed.Target);
    }
    [Fact]
    public void Approve_PendingRequest_ReturnsVacationLinkedToRequest()
    {
        var original = Request();
        var id = new VacationId(Guid(51));
        var approved = Ok(original.Approve(id, "Одобрено"));
        Assert.Equal(new VacationRequestStatus.Approved(id), approved.Request.Status);
        Assert.Equal(original.Id, approved.Vacation.RequestId);
        Assert.Equal(original.Period, approved.Vacation.Period);
        AssertError(new VacationError.RequestAlreadyDecided(), approved.Request.Withdraw());
        AssertError(new VacationError.RequestAlreadyDecided(), approved.Request.Edit(Period(Utc(7, 15), Utc(8, 15)), null));
    }
    [Fact]
    public void Withdraw_PendingRequest_PreventsLaterApproval() => AssertError(new VacationError.RequestAlreadyDecided(), Ok(Request().Withdraw()).Approve(new(Guid(51)), null));
    [Fact]
    public void CreateChange_ApprovedVacation_RequestsSameIdentityWithoutApplyingChange()
    {
        var vacation = MakeVacation(51, new VacationSubject.Client(TestData.ClientId), Utc(5, 15), Utc(7, 15));
        var request = Ok(VacationRequestRules.CreateChange(new(Guid(50)), vacation, Period(Utc(5, 15), Utc(6, 15)), null));
        Assert.Equal(vacation.Id, request.Target);
        Assert.Equal(Utc(7, 15), vacation.Period.EndsAt);
        AssertError(new VacationError.WrongVacation(), request.Approve(new(Guid(52)), null));
    }
    [Fact]
    public void Reject_PendingRequest_KeepsClientFacingComment()
    {
        var rejected = Ok(Request().Reject("Выберите другие даты"));
        Assert.IsType<VacationRequestStatus.Rejected>(rejected.Status);
        Assert.Equal("Выберите другие даты", rejected.ResponseComment);
    }

    [Fact]
    public void Approve_ChangeRequest_RequiresCurrentVacation()
    {
        var vacation = MakeVacation(51, new VacationSubject.Client(TestData.ClientId), Utc(5, 15), Utc(7, 15));
        var request = Ok(VacationRequestRules.CreateChange(new(Guid(50)), vacation, Period(Utc(5, 15), Utc(6, 15)), null));

        AssertError(new VacationError.CurrentVacationRequired(), request.Approve(vacation.Id, null));
        Assert.IsType<VacationRequestStatus.Pending>(request.Status);
    }

    [Fact]
    public void ApproveChange_ActiveVacation_ChangesPeriodAndPreservesOrigin()
    {
        var originalApproval = Ok(Request().Approve(new(Guid(51)), null));
        var vacation = originalApproval.Vacation;
        var period = Period(Utc(5, 15).AddMinutes(30), Utc(6, 14));
        var request = Ok(VacationRequestRules.CreateChange(new(Guid(52)), vacation, period, "Раннее завершение"));

        var approval = Ok(request.ApproveChange(vacation, "  Согласовано  "));

        Assert.Equal(vacation.Id, approval.Vacation.Id);
        Assert.Equal(vacation.Subject, approval.Vacation.Subject);
        Assert.Equal(vacation.RequestId, approval.Vacation.RequestId);
        Assert.Equal(period, approval.Vacation.Period);
        Assert.Equal(new VacationRequestStatus.Approved(vacation.Id), approval.Request.Status);
        Assert.Equal("Согласовано", approval.Request.ResponseComment);
        Assert.Equal(originalApproval.Request.Period, vacation.Period);
        Assert.IsType<VacationRequestStatus.Pending>(request.Status);
    }

    [Fact]
    public void ApproveChange_VacationDeletedAfterSubmission_RejectsApproval()
    {
        var vacation = MakeVacation(51, new VacationSubject.Client(TestData.ClientId), Utc(5, 15), Utc(7, 15));
        var request = Ok(VacationRequestRules.CreateChange(new(Guid(50)), vacation, Period(Utc(5, 15), Utc(6, 15)), null));

        AssertError(new VacationError.VacationDeleted(), request.ApproveChange(vacation.Remove(), null));
    }

    [Fact]
    public void ApproveChange_DifferentVacation_RejectsApproval()
    {
        var vacation = MakeVacation(51, new VacationSubject.Client(TestData.ClientId), Utc(5, 15), Utc(7, 15));
        var request = Ok(VacationRequestRules.CreateChange(new(Guid(50)), vacation, Period(Utc(5, 15), Utc(6, 15)), null));
        var other = MakeVacation(52, vacation.Subject, Utc(5, 15), Utc(7, 15));

        AssertError(new VacationError.WrongVacation(), request.ApproveChange(other, null));
    }

    [Fact]
    public void ApproveChange_DifferentSubjectWithSameIdentity_RejectsApproval()
    {
        var vacation = MakeVacation(51, new VacationSubject.Client(TestData.ClientId), Utc(5, 15), Utc(7, 15));
        var request = Ok(VacationRequestRules.CreateChange(new(Guid(50)), vacation, Period(Utc(5, 15), Utc(6, 15)), null));
        var other = MakeVacation(51, new VacationSubject.Staff(TestData.StaffId), Utc(5, 15), Utc(7, 15));

        AssertError(new VacationError.WrongVacation(), request.ApproveChange(other, null));
    }

    [Fact]
    public void ApproveChange_NewVacationRequest_RejectsApproval()
    {
        var vacation = MakeVacation(51, new VacationSubject.Client(TestData.ClientId), Utc(5, 15), Utc(7, 15));

        AssertError(new VacationError.WrongVacation(), Request().ApproveChange(vacation, null));
    }

    [Fact]
    public void ApproveChange_ApprovedRequest_RejectsRepeatedDecision()
    {
        var vacation = MakeVacation(51, new VacationSubject.Client(TestData.ClientId), Utc(5, 15), Utc(7, 15));
        var request = Ok(VacationRequestRules.CreateChange(new(Guid(50)), vacation, Period(Utc(5, 15), Utc(6, 15)), null));
        var approval = Ok(request.ApproveChange(vacation, null));

        AssertError(new VacationError.RequestAlreadyDecided(), approval.Request.ApproveChange(approval.Vacation, null));
    }
}
