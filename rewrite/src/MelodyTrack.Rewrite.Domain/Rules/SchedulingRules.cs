using CSharpFunctionalExtensions;

namespace MelodyTrack.Rewrite.Domain.Rules;

public static class SeriesRules
{
    public static Result<Series, ErrorList> Create(SeriesId id, ClientId clientId, ServiceId serviceId, LessonDuration duration, DateOnly startsOn, SeriesEnd end, SeriesRule rule)
    {
        var ids = Validation.Collect(Validation.Identifier(id.Value), Validation.Identifier(clientId.Value, "clientId"),
            Validation.Identifier(serviceId.Value, "serviceId"), Validation.Identifier(rule.StaffId.Value, "staffId"),
            rule.RoomId is { } room ? Validation.Identifier(room.Value, "roomId") : UnitResult.Success<ErrorList>());
        if (ids.IsFailure) return ids.Error;
        if (end is SeriesEnd.OnDateInclusive ending && ending.Date < startsOn)
            return ErrorList.Single(new SchedulingError.SeriesEndBeforeStart(startsOn, ending.Date));
        return new Series(id, clientId, serviceId, duration, startsOn, end, rule);
    }

    public static Result<Series, ErrorList> Finish(this Series model, DateOnly fromDate)
    {
        if (model.TerminatedFrom is { } previous && previous != fromDate) return ErrorList.Single(new SchedulingError.SeriesFinished());
        return model with { TerminatedFrom = fromDate };
    }

    public static Result<Series, ErrorList> ChangeRule(this Series model, SeriesRule rule)
    {
        if (model.TerminatedFrom.HasValue) return ErrorList.Single(new SchedulingError.SeriesFinished());
        var ids = Validation.Collect(Validation.Identifier(rule.StaffId.Value, "staffId"),
            rule.RoomId is { } room ? Validation.Identifier(room.Value, "roomId") : UnitResult.Success<ErrorList>());
        if (ids.IsFailure) return ids.Error;
        return model with { Rule = rule };
    }

    public static Result<SeriesStep?, ErrorList> NextCandidate(this Series model, int countedAppointments)
    {
        if (countedAppointments < 0) return ErrorList.Single(new SchedulingError.InvalidCountedAppointments());
        var next = model.NextDate is { } date ? model.Rule.Weekly.NextOnOrAfter(date) : null;
        if (next is null || model.End is SeriesEnd.AfterAppointments count && countedAppointments >= count.Count.Value
            || model.TerminatedFrom is { } terminated && next.Date >= terminated
            || model.End is SeriesEnd.OnDateInclusive ending && next.Date > ending.Date)
            return Result.Success<SeriesStep?, ErrorList>(null);
        var startsAt = SchoolTime.At(next.Date, next.StartHour.ToTimeOnly());
        if (startsAt.IsFailure)
        {
            var errors = new ErrorListBuilder()
                .AddRange(startsAt.Error.Issues.Select(issue => new DomainIssue(new SchedulingError.TimeConversionFailed((TimeError)issue.Error))));
            return errors.ToResult().Error;
        }
        var number = model.NextSequenceNumber.Next();
        if (number.IsFailure) return number.Error;
        var advanced = model with { NextSequenceNumber = number.Value, NextDate = next.Date == DateOnly.MaxValue ? null : next.Date.AddDays(1) };
        return new SeriesStep(new(model.NextSequenceNumber, startsAt.Value), advanced);
    }
}
